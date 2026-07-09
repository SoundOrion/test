# Temporal 起動手順 README

この README は、社内オフライン minikube 環境で **Temporal Server** と **Temporal UI** を起動し、UI / Frontend API にアクセスするまでの手順です。

PowerShell での実行を前提にしています。

---

## 0. 共通変数

まず PowerShell で以下を実行します。

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$HELM="C:\Users\Hatsuyama\Desktop\k8s\tools\helm\helm.exe"
$KUBECTL="C:\Users\Hatsuyama\Desktop\k8s\tools\kubectl\kubectl.exe"
$MINIKUBE="C:\Users\Hatsuyama\Desktop\k8s\tools\minikube\minikube.exe"
$KUBECONFIG_PATH="C:\Users\Hatsuyama\Desktop\k8s\kube\config"

$env:KUBECONFIG=$KUBECONFIG_PATH

cd $BASE
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get nodes
& $HELM --kubeconfig $KUBECONFIG_PATH list -A
```

`minikube Ready` または `kubectl get nodes` で node が見えれば OK です。

---

## 1. 前提ファイル

この README では、以下のファイルがある前提です。

```text
chart:
  .\charts\temporal-*.tgz

values:
  .\values\temporal-values.yaml

postgres manifest:
  .\manifests\temporal-postgres.yaml
```

chart のファイル名は環境によって違うため、PowerShell では変数に入れて使います。

```powershell
$TEMPORAL_CHART=(Get-ChildItem .\charts\temporal-*.tgz | Select-Object -First 1).FullName
$TEMPORAL_VALUES=".\values\temporal-values.yaml"

Write-Host $TEMPORAL_CHART
Write-Host $TEMPORAL_VALUES
```

---

## 2. minikube 側に image が入っているか確認

Temporal に必要な image が minikube 側に入っているか確認します。

```powershell
& $MINIKUBE image ls --profile=minikube
```

ただし、この環境では `$MINIKUBE image load` が profile を見失うことがあるため、確実に確認するならこちらでも確認できます。

```powershell
docker exec minikube docker images | Select-String "temporal|postgres|elasticsearch|admin-tools|ui"
```

values から参照 image を確認します。

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH template temporal $TEMPORAL_CHART `
  -f $TEMPORAL_VALUES `
  --namespace temporal `
  | Select-String "image:"
```

最低限、利用している values に応じて以下の系統の image が minikube 側に入っていれば OK です。

```text
temporalio/server:<tag>
temporalio/admin-tools:<tag>
temporalio/ui:<tag>
postgres または bitnamilegacy/postgresql:<tag>
```

この環境では PostgreSQL を `temporal-postgres.yaml` で用意する想定です。

---

## 3. image を minikube に load する方法

### 方法A: `$MINIKUBE image load` が使える場合

tar ファイルがある場合は以下です。

```powershell
& $MINIKUBE image load .\images\temporal-images.tar --profile=minikube
```

### 方法B: Docker Desktop にある image を minikube ノードへ入れる場合

この環境ではこちらの方法が安定しました。

```powershell
cd $BASE

$images = @(
  "temporalio/server:<tag>",
  "temporalio/admin-tools:<tag>",
  "temporalio/ui:<tag>",
  "postgres:<tag>"
)

mkdir .\images -Force | Out-Null

foreach ($image in $images) {
  Write-Host ""
  Write-Host "==== Loading $image ====" -ForegroundColor Cyan

  docker image inspect $image > $null 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Host "SKIP: local Docker にありません: $image" -ForegroundColor Yellow
    continue
  }

  $safeName = [regex]::Replace($image, "[/:@]", "_")
  $tar = ".\images\$safeName.tar"
  $remote = "/home/docker/$safeName.tar"

  docker save $image -o $tar
  docker cp $tar "minikube:$remote"
  docker exec minikube docker load -i $remote
  docker exec minikube rm $remote
  Remove-Item $tar

  Write-Host "OK: $image" -ForegroundColor Green
}
```

`<tag>` は実際に values で参照している tag に置き換えます。確認します。

```powershell
docker exec minikube docker images | Select-String "temporal|postgres|admin-tools|ui"
```

---

# Temporal 起動手順

Temporal は `temporal-values.yaml` を使います。

```text
values:
  .\values\temporal-values.yaml

chart:
  .\charts\temporal-*.tgz

namespace:
  temporal
```

---

## 4. Temporal PostgreSQL を用意する

Temporal Server は永続化用の PostgreSQL が必要です。まず Pod があるか確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal
```

`temporal-postgres-0` がなければ、manifest を apply します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f .\manifests\temporal-postgres.yaml
```

起動確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal -w
```

以下になれば OK です。

```text
temporal-postgres-0   1/1   Running
```

watch を止めるには `Ctrl + C` です。

---

## 5. Temporal 用 database を確認する

PostgreSQL 内の database 一覧を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal temporal-postgres-0 -- `
  psql -U temporal -d postgres -c "\l"
```

以下があれば OK です。

```text
temporal
temporal_visibility
```

もし database がなければ作成します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal temporal-postgres-0 -- `
  psql -U temporal -d postgres -c "CREATE DATABASE temporal OWNER temporal;"

& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal temporal-postgres-0 -- `
  psql -U temporal -d postgres -c "CREATE DATABASE temporal_visibility OWNER temporal;"
```

すでに存在する場合はエラーになります。その場合は無視して OK です。

---

## 6. Temporal values の image 確認

Helm template で参照 image を確認します。

```powershell
$TEMPORAL_CHART=(Get-ChildItem .\charts\temporal-*.tgz | Select-Object -First 1).FullName
$TEMPORAL_VALUES=".\values\temporal-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH template temporal $TEMPORAL_CHART `
  --namespace temporal `
  -f $TEMPORAL_VALUES `
  | Select-String "image:"
```

表示された image が minikube に入っていることを確認します。

```powershell
docker exec minikube docker images | Select-String "temporal|postgres|admin-tools|ui"
```

---

## 7. Temporal を Helm install / upgrade する

```powershell
$TEMPORAL_CHART=(Get-ChildItem .\charts\temporal-*.tgz | Select-Object -First 1).FullName
$TEMPORAL_VALUES=".\values\temporal-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install temporal $TEMPORAL_CHART `
  --namespace temporal `
  --create-namespace `
  -f $TEMPORAL_VALUES `
  --timeout 30m
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n temporal
```

期待値です。

```text
temporal-frontend-xxxxx       1/1   Running
temporal-history-xxxxx        1/1   Running
temporal-matching-xxxxx       1/1   Running
temporal-worker-xxxxx         1/1   Running
temporal-web-xxxxx            1/1   Running
temporal-admintools-xxxxx     1/1   Running
temporal-postgres-0           1/1   Running
```

chart / values によって Pod 名は多少変わります。

---

## 8. Temporal UI を開く

Temporal UI の Service 名を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n temporal
```

`temporal-web` がある場合は以下です。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n temporal svc/temporal-web 8082:8080
```

ブラウザで開きます。

```text
http://localhost:8082
```

`temporal-ui` という Service 名の場合は、Service 名を置き換えます。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n temporal svc/temporal-ui 8082:8080
```

---

## 9. Temporal Frontend に接続する

C# Worker などから接続する Temporal Frontend は通常 `7233` です。

minikube 内から使う場合:

```text
temporal-frontend.temporal.svc.cluster.local:7233
```

ローカルPCから一時的に接続する場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n temporal svc/temporal-frontend 7233:7233
```

接続先:

```text
localhost:7233
```

---

## 10. 動作確認

admin tools Pod がある場合は、そこから確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal | Select-String "admin"
```

Temporal CLI が入っている場合:

```powershell
$ADMIN_POD=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal --no-headers `
  | Select-String "admintools" `
  | ForEach-Object { ($_ -split "\s+")[0] } `
  | Select-Object -First 1)

& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal $ADMIN_POD -- `
  temporal operator cluster health --address temporal-frontend:7233
```

`tctl` が入っている古い admin-tools の場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal $ADMIN_POD -- `
  tctl --address temporal-frontend:7233 namespace list
```

---

# トラブルシュート

## Helm が `127.0.0.1:6443` を見に行く

エラー例:

```text
Kubernetes cluster unreachable:
Get "https://127.0.0.1:6443/version":
connectex: No connection could be made
```

原因は Helm が正しい kubeconfig を見ていないことです。

対策:

```powershell
$env:KUBECONFIG=$KUBECONFIG_PATH
```

または Helm に毎回 `--kubeconfig` を付けます。

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH list -A
```

---

## Temporal Server が DB 接続で落ちる

ログ確認:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n temporal -l app.kubernetes.io/name=temporal --all-containers --tail=200
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n temporal temporal-postgres-0 --tail=200
```

確認ポイント:

```text
- temporal-postgres-0 が Running か
- temporal / temporal_visibility database があるか
- temporal-values.yaml の host / port / user / password / database が正しいか
- secret 名が values と一致しているか
```

---

## Temporal UI が開かない

Service 名と port を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n temporal
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe svc -n temporal temporal-web
```

UI Pod のログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n temporal -l app.kubernetes.io/component=web --all-containers --tail=200
```

label が合わない場合は Pod 一覧から対象 Pod 名を指定します。

---

## image pull で止まる

オフライン環境では image が minikube ノードに入っていないと `ImagePullBackOff` になります。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n temporal <pod-name>
docker exec minikube docker images | Select-String "temporal|postgres|ui"
```

足りない image を Docker Desktop から minikube ノードへ入れます。

---

## Temporal を削除する

Temporal:

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH uninstall temporal -n temporal
```

namespace ごと消す場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete namespace temporal
```

PVC が残る場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -A
```

削除例:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pvc <pvc-name> -n temporal
```

---

# 最短コマンドまとめ

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$HELM="C:\Users\Hatsuyama\Desktop\k8s\tools\helm\helm.exe"
$KUBECTL="C:\Users\Hatsuyama\Desktop\k8s\tools\kubectl\kubectl.exe"
$MINIKUBE="C:\Users\Hatsuyama\Desktop\k8s\tools\minikube\minikube.exe"
$KUBECONFIG_PATH="C:\Users\Hatsuyama\Desktop\k8s\kube\config"

$env:KUBECONFIG=$KUBECONFIG_PATH

cd $BASE

$TEMPORAL_CHART=(Get-ChildItem .\charts\temporal-*.tgz | Select-Object -First 1).FullName
$TEMPORAL_VALUES=".\values\temporal-values.yaml"

& $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f .\manifests\temporal-postgres.yaml

& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n temporal temporal-postgres-0 -- `
  psql -U temporal -d postgres -c "\l"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install temporal $TEMPORAL_CHART `
  --namespace temporal `
  --create-namespace `
  -f $TEMPORAL_VALUES `
  --timeout 30m

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n temporal
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n temporal

& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n temporal svc/temporal-web 8082:8080
```

Temporal UI:

```text
http://localhost:8082
```

Temporal Frontend:

```text
temporal-frontend.temporal.svc.cluster.local:7233
```
