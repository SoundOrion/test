# Kafka 起動手順 README

この README は、社内オフライン minikube 環境で **Apache Kafka** を起動し、topic 作成・produce / consume まで確認する手順です。

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
  .\charts\kafka-*.tgz

values:
  .\values\kafka-values.yaml

namespace:
  kafka
```

chart のファイル名は環境によって違うため、PowerShell では変数に入れて使います。

```powershell
$KAFKA_CHART=(Get-ChildItem .\charts\kafka-*.tgz | Select-Object -First 1).FullName
$KAFKA_VALUES=".\values\kafka-values.yaml"

Write-Host $KAFKA_CHART
Write-Host $KAFKA_VALUES
```

---

## 2. minikube 側に image が入っているか確認

Kafka に必要な image が minikube 側に入っているか確認します。

```powershell
& $MINIKUBE image ls --profile=minikube
```

ただし、この環境では `$MINIKUBE image load` が profile を見失うことがあるため、確実に確認するならこちらでも確認できます。

```powershell
docker exec minikube docker images | Select-String "kafka|zookeeper|bitnami|bitnamilegacy"
```

values から参照 image を確認します。

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH template kafka $KAFKA_CHART `
  -f $KAFKA_VALUES `
  --namespace kafka `
  | Select-String "image:"
```

最低限、利用している values に応じて以下の系統の image が minikube 側に入っていれば OK です。

```text
bitnami/kafka:<tag> または bitnamilegacy/kafka:<tag>
bitnami/zookeeper:<tag> または bitnamilegacy/zookeeper:<tag>  # ZooKeeper 構成の場合のみ
bitnami/os-shell:<tag> または bitnamilegacy/os-shell:<tag>      # chart が init container で使う場合
```

KRaft 構成の場合、ZooKeeper は不要です。

---

## 3. image を minikube に load する方法

### 方法A: `$MINIKUBE image load` が使える場合

```powershell
& $MINIKUBE image load .\images\kafka-images.tar --profile=minikube
```

### 方法B: Docker Desktop にある image を minikube ノードへ入れる場合

この環境ではこちらの方法が安定しました。

```powershell
cd $BASE

$images = @(
  "bitnamilegacy/kafka:<tag>",
  "bitnamilegacy/zookeeper:<tag>",
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
docker exec minikube docker images | Select-String "kafka|zookeeper|bitnami|bitnamilegacy"
```

---

# Kafka 起動手順

Kafka は `kafka-values.yaml` を使います。

```text
values:
  .\values\kafka-values.yaml

chart:
  .\charts\kafka-*.tgz

namespace:
  kafka
```

---

## 4. Kafka values の image 確認

まず Helm template で参照 image を確認します。

```powershell
$KAFKA_CHART=(Get-ChildItem .\charts\kafka-*.tgz | Select-Object -First 1).FullName
$KAFKA_VALUES=".\values\kafka-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH template kafka $KAFKA_CHART `
  --namespace kafka `
  -f $KAFKA_VALUES `
  | Select-String "image:"
```

表示された image が minikube に入っていることを確認します。

```powershell
docker exec minikube docker images | Select-String "kafka|zookeeper|bitnami|bitnamilegacy"
```

---

## 5. Kafka を Helm install / upgrade する

```powershell
$KAFKA_CHART=(Get-ChildItem .\charts\kafka-*.tgz | Select-Object -First 1).FullName
$KAFKA_VALUES=".\values\kafka-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install kafka $KAFKA_CHART `
  --namespace kafka `
  --create-namespace `
  -f $KAFKA_VALUES `
  --timeout 30m
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

期待値です。

```text
kafka-controller-0       1/1   Running    # KRaft 構成の場合
```

ZooKeeper 構成の場合は以下のような Pod も出ます。

```text
kafka-0                  1/1   Running
kafka-zookeeper-0        1/1   Running
```

chart / values によって Pod 名は変わります。

---

## 6. Kafka broker の接続先を確認する

Service を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

minikube 内の Pod から接続する場合は、だいたい以下のどちらかです。

```text
kafka.kafka.svc.cluster.local:9092
kafka-controller.kafka.svc.cluster.local:9092
```

正確な Service 名は `kubectl get svc -n kafka` の結果に合わせます。

注意:

```text
Kafka をローカルPCから port-forward で使う場合、advertised.listeners の設定が重要です。
まずは in-cluster の client Pod から動作確認するのが安全です。
```

---

## 7. client Pod から topic 作成を確認する

Kafka image を使って、一時的な client Pod を起動します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH run kafka-client `
  --namespace kafka `
  --restart=Never `
  --image=bitnamilegacy/kafka:<tag> `
  --command -- sleep 3600
```

`<tag>` は minikube に入っている Kafka image の tag に置き換えます。

Pod 起動確認:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pod -n kafka kafka-client
```

broker を変数に入れます。Service 名は環境に合わせてください。

```powershell
$BOOTSTRAP="kafka.kafka.svc.cluster.local:9092"
```

Topic を作成します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n kafka kafka-client -- `
  kafka-topics.sh --bootstrap-server $BOOTSTRAP `
  --create --if-not-exists `
  --topic test-topic `
  --partitions 1 `
  --replication-factor 1
```

Topic 一覧を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n kafka kafka-client -- `
  kafka-topics.sh --bootstrap-server $BOOTSTRAP --list
```

---

## 8. produce / consume を確認する

メッセージを produce します。

```powershell
"hello from kafka on minikube" | & $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -i -n kafka kafka-client -- `
  kafka-console-producer.sh --bootstrap-server $BOOTSTRAP --topic test-topic
```

メッセージを consume します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n kafka kafka-client -- `
  kafka-console-consumer.sh --bootstrap-server $BOOTSTRAP `
  --topic test-topic `
  --from-beginning `
  --timeout-ms 10000
```

以下が見えれば OK です。

```text
hello from kafka on minikube
```

client Pod は不要になったら削除します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pod -n kafka kafka-client
```

---

## 9. ローカルPCから接続したい場合

まず Service 名を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

port-forward します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH port-forward -n kafka svc/kafka 9092:9092
```

ただし Kafka は `advertised.listeners` の都合で、単純な port-forward だけでは外部クライアントから接続できないことがあります。

ローカルPCから使う場合は、values 側で以下のような方針に揃えます。

```text
- localhost:9092 を advertised listener として公開する
- または NodePort / LoadBalancer 用 listener を別途設定する
- ローカル検証だけなら in-cluster client Pod で確認する
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

## Kafka Pod が Pending のまま

PVC / storage / resource の問題が多いです。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n kafka <pod-name>
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -n kafka
```

minikube の storage addon や PVC 状態を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get storageclass
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pvc -n kafka <pvc-name>
```

---

## image pull で止まる

オフライン環境では image が minikube ノードに入っていないと `ImagePullBackOff` になります。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe pod -n kafka <pod-name>
docker exec minikube docker images | Select-String "kafka|zookeeper|bitnami|bitnamilegacy"
```

足りない image を Docker Desktop から minikube ノードへ入れます。

---

## client Pod から broker に接続できない

Service 名と port を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

Pod 内から名前解決を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n kafka kafka-client -- `
  bash -lc "getent hosts kafka.kafka.svc.cluster.local || true"
```

broker のログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n kafka <kafka-pod-name> --tail=200
```

KRaft / ZooKeeper の構成により Service 名が違うため、`$BOOTSTRAP` を実際の Service 名に合わせます。

---

## Kafka を削除する

Kafka:

```powershell
& $HELM --kubeconfig $KUBECONFIG_PATH uninstall kafka -n kafka
```

namespace ごと消す場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete namespace kafka
```

PVC が残る場合:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pvc -A
```

削除例:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pvc <pvc-name> -n kafka
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

$KAFKA_CHART=(Get-ChildItem .\charts\kafka-*.tgz | Select-Object -First 1).FullName
$KAFKA_VALUES=".\values\kafka-values.yaml"

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install kafka $KAFKA_CHART `
  --namespace kafka `
  --create-namespace `
  -f $KAFKA_VALUES `
  --timeout 30m

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

in-cluster 接続先例:

```text
kafka.kafka.svc.cluster.local:9092
```
