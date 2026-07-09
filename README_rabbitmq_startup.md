# RabbitMQ 起動手順 README

この README は、社内オフライン minikube 環境で **RabbitMQ** を起動し、Management UI / AMQP port にアクセスするまでの手順です。

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
  .\charts\rabbitmq-*.tgz

values:
  .\values\rabbitmq-values.yaml

namespace:
  rabbitmq
```

chart のファイル名は環境によって違うため、PowerShell では変数に入れて使います。

```powershell
$RABBITMQ_CHART=(Get-ChildItem .\charts\rabbitmq-*.tgz | Select-Object -First 1).FullName
$RABBITMQ_VALUES=".\values\rabbitmq-values.yaml"

Write-Host $RABBITMQ_CHART
Write-Host $RABBITMQ_VALUES
```

---

## 2. minikube 側に image が入っているか確認

RabbitMQ に必要な image が minikube 側に入っているか確認します。

```powershell
& $MINIKUBE image ls --profile=minikube
```

ただし、この環境では `$MINIKUBE image load` が profile を見失うことがあるため、確実に確認するならこちらでも確認できます。

```powershell
docker exec minikube docker images | Select-String "rabbitmq|bitnami|bitnamilegacy"
```

values から参照 image を確認します。

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH template rabbitmq $RABBITMQ_CHART `
  -f $RABBITMQ_VALUES `
  --namespace rabbitmq `
  | Select-String "image:"
```

最低限、利用している values に応じて以下の系統の image が minikube 側に入っていれば OK です。

```text
bitnami/rabbitmq:<tag> または bitnamilegacy/rabbitmq:<tag>
bitnami/os-shell:<tag> または bitnamilegacy/os-shell:<tag>   # chart が init container で使う場合
```

---

## 3. image を minikube に load する方法

### 方法A: `$MINIKUBE image load` が使える場合

```powershell
& $MINIKUBE image load .\images\rabbitmq-images.tar --profile=minikube
```

### 方法B: Docker Desktop にある image を minikube ノードへ入れる場合

この環境ではこちらの方法が安定しました。

```powershell
cd $BASE

$images = @(
  "bitnamilegacy/rabbitmq:<tag>",
  "bitnamilegacy/os-shell:<tag>"
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
docker exec minikube docker images | Select-String "rabbitmq|bitnami|bitnamilegacy"
```

---

# RabbitMQ 起動手順

RabbitMQ は `rabbitmq-values.yaml` を使います。

```text
values:
  .\values\rabbitmq-values.yaml

chart:
  .\charts\rabbitmq-*.tgz

namespace:
  rabbitmq
```

---

## 4. RabbitMQ values の image 確認

まず Helm template で参照 image を確認します。

```powershell
$RABBITMQ_CHART=(Get-ChildItem .\charts\rabbitmq-*.tgz | Select-Object -First 1).FullName
$RABBITMQ_VALUES=".\values\rabbitmq-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH template rabbitmq $RABBITMQ_CHART `
  --namespace rabbitmq `
  -f $RABBITMQ_VALUES `
  | Select-String "image:"
```

表示された image が minikube に入っていることを確認します。

```powershell
docker exec minikube docker images | Select-String "rabbitmq|bitnami|bitnamilegacy"
```

---

## 5. RabbitMQ を Helm install / upgrade する

```powershell
$RABBITMQ_CHART=(Get-ChildItem .\charts\rabbitmq-*.tgz | Select-Object -First 1).FullName
$RABBITMQ_VALUES=".\values\rabbitmq-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install rabbitmq $RABBITMQ_CHART `
  --namespace rabbitmq `
  --create-namespace `
  -f $RABBITMQ_VALUES `
  --timeout 30m
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
```

期待値です。

```text
rabbitmq-0       1/1   Running
```

replica を増やしている場合は `rabbitmq-1`, `rabbitmq-2` も Running になります。

---

## 6. RabbitMQ の接続先を確認する

Service を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
```

minikube 内の Pod から接続する場合は、だいたい以下です。

```text
AMQP:
  rabbitmq.rabbitmq.svc.cluster.local:5672

Management UI:
  rabbitmq.rabbitmq.svc.cluster.local:15672
```

Service 名が違う場合は `kubectl get svc -n rabbitmq` の結果に合わせます。

---

## 7. RabbitMQ のログイン情報を確認する

Bitnami 系 chart の場合、ユーザー名は `user`、パスワードは Secret に入っていることが多いです。

Secret 一覧を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq
```

`rabbitmq` という Secret がある場合:

```powershell
$RABBITMQ_PASSWORD_B64=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq rabbitmq `
  -o jsonpath="{.data.rabbitmq-password}")

$RABBITMQ_PASSWORD=[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($RABBITMQ_PASSWORD_B64))

Write-Host $RABBITMQ_PASSWORD
```

Secret 名が違う場合は、`kubectl get secret -n rabbitmq` の結果に合わせて置き換えます。

values で固定ユーザー / パスワードを指定している場合は、その値を使います。

---

## 8. RabbitMQ Management UI を開く

Management UI を port-forward します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n rabbitmq svc/rabbitmq 15672:15672
```

ブラウザで開きます。

```text
http://localhost:15672
```

ログイン例:

```text
username: user
password: <Secret から取得した rabbitmq-password>
```

---

## 9. AMQP port をローカルPCへ公開する

C# アプリやローカル client から接続する場合は AMQP port も port-forward します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n rabbitmq svc/rabbitmq 5672:5672
```

接続先:

```text
amqp://user:<password>@localhost:5672/
```

minikube 内の Pod から接続する場合:

```text
amqp://user:<password>@rabbitmq.rabbitmq.svc.cluster.local:5672/
```

---

## 10. Pod 内で状態確認する

RabbitMQ Pod 名を取得します。

```powershell
$RABBITMQ_POD=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq `
  -o jsonpath="{.items[0].metadata.name}")

Write-Host $RABBITMQ_POD
```

status を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n rabbitmq $RABBITMQ_POD -- `
  rabbitmq-diagnostics status
```

queue 一覧を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n rabbitmq $RABBITMQ_POD -- `
  rabbitmqctl list_queues
```

plugin を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n rabbitmq $RABBITMQ_POD -- `
  rabbitmq-plugins list
```

---

## 11. 簡易 publish / consume 確認

Management UI から簡単に確認できます。

1. `http://localhost:15672` を開く
2. `Queues and Streams` を開く
3. `Add a new queue` で `test-queue` を作成
4. 作成した queue を開く
5. `Publish message` で message を publish
6. `Get messages` で message を consume

ローカルの C# アプリから確認する場合は、AMQP port-forward を有効にして以下へ接続します。

```text
amqp://user:<password>@localhost:5672/
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

## RabbitMQ Pod が Pending のまま

PVC / storage / resource の問題が多いです。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n rabbitmq <pod-name>
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -n rabbitmq
```

minikube の storage addon や PVC 状態を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get storageclass
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pvc -n rabbitmq <pvc-name>
```

---

## image pull で止まる

オフライン環境では image が minikube ノードに入っていないと `ImagePullBackOff` になります。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n rabbitmq <pod-name>
docker exec minikube docker images | Select-String "rabbitmq|bitnami|bitnamilegacy"
```

足りない image を Docker Desktop から minikube ノードへ入れます。

---

## Management UI にログインできない

確認ポイント:

```text
- port-forward が生きているか
- Service に 15672 があるか
- username / password が values または Secret と一致しているか
- rabbitmq_management plugin が有効か
```

Service を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
```

Secret を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq
```

ログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n rabbitmq $RABBITMQ_POD --tail=200
```

---

## AMQP 接続できない

確認ポイント:

```text
- port-forward で 5672 を開けているか
- 接続先が localhost:5672 になっているか
- username / password / vhost が正しいか
- Service 名が rabbitmq ではない場合、接続先を置き換えているか
```

Pod 内の listener を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n rabbitmq $RABBITMQ_POD -- `
  rabbitmq-diagnostics listeners
```

---

## RabbitMQ を削除する

RabbitMQ:

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH uninstall rabbitmq -n rabbitmq
```

namespace ごと消す場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete namespace rabbitmq
```

PVC が残る場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -A
```

削除例:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pvc <pvc-name> -n rabbitmq
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

$RABBITMQ_CHART=(Get-ChildItem .\charts\rabbitmq-*.tgz | Select-Object -First 1).FullName
$RABBITMQ_VALUES=".\values\rabbitmq-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install rabbitmq $RABBITMQ_CHART `
  --namespace rabbitmq `
  --create-namespace `
  -f $RABBITMQ_VALUES `
  --timeout 30m

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq

& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n rabbitmq svc/rabbitmq 15672:15672
```

RabbitMQ Management UI:

```text
http://localhost:15672

username: user
password: <Secret から取得した rabbitmq-password>
```

AMQP:

```text
amqp://user:<password>@localhost:5672/
amqp://user:<password>@rabbitmq.rabbitmq.svc.cluster.local:5672/
```
