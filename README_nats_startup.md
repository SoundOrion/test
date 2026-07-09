# NATS 起動手順 README

この README は、社内オフライン minikube 環境で **NATS** を起動し、publish / subscribe まで確認する手順です。

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
  .\charts\nats-*.tgz

values:
  .\values\nats-values.yaml

namespace:
  nats
```

chart のファイル名は環境によって違うため、PowerShell では変数に入れて使います。

```powershell
$NATS_CHART=(Get-ChildItem .\charts\nats-*.tgz | Select-Object -First 1).FullName
$NATS_VALUES=".\values\nats-values.yaml"

Write-Host $NATS_CHART
Write-Host $NATS_VALUES
```

---

## 2. minikube 側に image が入っているか確認

NATS に必要な image が minikube 側に入っているか確認します。

```powershell
& $MINIKUBE image ls --profile=minikube
```

ただし、この環境では `$MINIKUBE image load` が profile を見失うことがあるため、確実に確認するならこちらでも確認できます。

```powershell
docker exec minikube docker images | Select-String "nats"
```

values から参照 image を確認します。

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH template nats $NATS_CHART `
  -f $NATS_VALUES `
  --namespace nats `
  | Select-String "image:"
```

最低限、利用している values に応じて以下の系統の image が minikube 側に入っていれば OK です。

```text
nats:<tag>
natsio/nats-box:<tag>    # 動作確認用 client として使う場合
```

---

## 3. image を minikube に load する方法

### 方法A: `$MINIKUBE image load` が使える場合

```powershell
& $MINIKUBE image load .\images\nats-images.tar --profile=minikube
```

### 方法B: Docker Desktop にある image を minikube ノードへ入れる場合

この環境ではこちらの方法が安定しました。

```powershell
cd $BASE

$images = @(
  "nats:<tag>",
  "natsio/nats-box:<tag>"
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
docker exec minikube docker images | Select-String "nats"
```

---

# NATS 起動手順

NATS は `nats-values.yaml` を使います。

```text
values:
  .\values\nats-values.yaml

chart:
  .\charts\nats-*.tgz

namespace:
  nats
```

---

## 4. NATS values の image 確認

まず Helm template で参照 image を確認します。

```powershell
$NATS_CHART=(Get-ChildItem .\charts\nats-*.tgz | Select-Object -First 1).FullName
$NATS_VALUES=".\values\nats-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH template nats $NATS_CHART `
  --namespace nats `
  -f $NATS_VALUES `
  | Select-String "image:"
```

表示された image が minikube に入っていることを確認します。

```powershell
docker exec minikube docker images | Select-String "nats"
```

---

## 5. NATS を Helm install / upgrade する

```powershell
$NATS_CHART=(Get-ChildItem .\charts\nats-*.tgz | Select-Object -First 1).FullName
$NATS_VALUES=".\values\nats-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install nats $NATS_CHART `
  --namespace nats `
  --create-namespace `
  -f $NATS_VALUES `
  --timeout 20m
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats
```

期待値です。

```text
nats-0        1/1   Running
```

replicas を増やしている場合は `nats-1`, `nats-2` も Running になります。

---

## 6. NATS の接続先を確認する

Service を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats
```

minikube 内の Pod から接続する場合は、だいたい以下です。

```text
nats://nats.nats.svc.cluster.local:4222
```

Service 名が違う場合は `kubectl get svc -n nats` の結果に合わせます。

---

## 7. port-forward でローカルPCから接続する

NATS client port:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n nats svc/nats 4222:4222
```

monitoring port を有効化している values の場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n nats svc/nats 8222:8222
```

ブラウザで monitoring を見る場合:

```text
http://localhost:8222
```

monitoring port が Service にない場合は、values 側で monitoring / monitor port を有効化してください。

---

## 8. nats-box で publish / subscribe を確認する

一時的な client Pod を起動します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH run nats-box `
  --namespace nats `
  --restart=Never `
  --image=natsio/nats-box:<tag> `
  --command -- sleep 3600
```

`<tag>` は minikube に入っている `natsio/nats-box` image の tag に置き換えます。

Pod 起動確認:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pod -n nats nats-box
```

NATS server に ping します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 server ping
```

メッセージを publish します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 pub test.subject "hello from nats on minikube"
```

subscribe を確認する場合は、別 PowerShell で以下を実行して待ち受けます。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -it -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 sub test.subject
```

別 PowerShell から publish します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 pub test.subject "hello again"
```

不要になったら client Pod を削除します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pod -n nats nats-box
```

---

## 9. JetStream を使う場合

values で JetStream を有効化している場合は、状態を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 server report jetstream
```

stream を作って確認する例です。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  nats --server nats://nats.nats.svc.cluster.local:4222 stream add TEST `
  --subjects "events.*" `
  --storage file `
  --retention limits `
  --discard old `
  --max-msgs=-1 `
  --max-bytes=-1 `
  --max-age=1h `
  --replicas 1 `
  --dupe-window=2m
```

対話入力を避けたい場合は values / nats CLI のバージョンに合わせてオプションを調整してください。

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

## NATS Pod が Pending のまま

PVC / storage / resource の問題が多いです。JetStream を file storage で使う場合は特に PVC を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n nats <pod-name>
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -n nats
```

---

## image pull で止まる

オフライン環境では image が minikube ノードに入っていないと `ImagePullBackOff` になります。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n nats <pod-name>
docker exec minikube docker images | Select-String "nats"
```

足りない image を Docker Desktop から minikube ノードへ入れます。

---

## nats-box から接続できない

Service 名と port を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats
```

Pod 内から名前解決を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n nats nats-box -- `
  sh -lc "getent hosts nats.nats.svc.cluster.local || true"
```

NATS Pod のログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n nats nats-0 --tail=200
```

---

## NATS を削除する

NATS:

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH uninstall nats -n nats
```

namespace ごと消す場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete namespace nats
```

PVC が残る場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -A
```

削除例:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pvc <pvc-name> -n nats
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

$NATS_CHART=(Get-ChildItem .\charts\nats-*.tgz | Select-Object -First 1).FullName
$NATS_VALUES=".\values\nats-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install nats $NATS_CHART `
  --namespace nats `
  --create-namespace `
  -f $NATS_VALUES `
  --timeout 20m

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats

& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n nats svc/nats 4222:4222
```

NATS 接続先:

```text
nats://localhost:4222
nats://nats.nats.svc.cluster.local:4222
```
