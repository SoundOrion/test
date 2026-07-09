# C# サンプル集: Temporal / Kafka / NATS / RabbitMQ on minikube

このサンプル集は、社内オフライン minikube 環境で起動済みの以下へ、C# アプリから接続するための最小サンプルです。

- Temporal
- Kafka
- NATS
- RabbitMQ

各サンプルは **.NET 8 console app** です。C# アプリは Kubernetes Pod / Job として minikube 内で動かし、Kubernetes Service DNS で各 backend に接続します。

## ディレクトリ構成

```text
csharp_samples_temporal_kafka_nats_rabbitmq/
├── temporal-sample/
├── kafka-sample/
├── nats-sample/
├── rabbitmq-sample/
├── k8s/
│   └── namespace.yaml
└── scripts/
    ├── build-and-load-all.ps1
    └── docker-save-load-to-minikube.ps1
```

## 前提

PowerShell で以下のような変数が使える前提です。

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$KUBECTL="C:\Users\Hatsuyama\Desktop\k8s\tools\kubectl\kubectl.exe"
$MINIKUBE="C:\Users\Hatsuyama\Desktop\k8s\tools\minikube\minikube.exe"
$KUBECONFIG_PATH="C:\Users\Hatsuyama\Desktop\k8s\kube\config"

$env:KUBECONFIG=$KUBECONFIG_PATH
```

各 backend の起動済み Service は以下を想定します。

| backend | namespace | service | port |
|---|---:|---:|---:|
| Temporal | `temporal` | `temporal-frontend` | `7233` |
| Kafka | `kafka` | `kafka` または `kafka-controller` | `9092` |
| NATS | `nats` | `nats` | `4222` |
| RabbitMQ | `rabbitmq` | `rabbitmq` | `5672` |

確認:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n temporal
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
```

## namespace 作成

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f .\k8s\namespace.yaml
```

## image build / load

一括で build して minikube に load する場合:

```powershell
.\scripts\build-and-load-all.ps1
```

`minikube image load` が profile を見失う環境では、script 内で Docker Desktop から minikube node へ `docker save/cp/load` する方式を使っています。

## オフライン NuGet について

このサンプルは NuGet package を使います。

- `Temporalio`
- `Confluent.Kafka`
- `NATS.Net`
- `RabbitMQ.Client`

完全オフライン環境では、社内 NuGet feed、または事前に取得済みの package cache が必要です。

Docker build 中に `dotnet restore` させる方式が難しい場合は、各サンプルで以下の流れにしてください。

```powershell
dotnet publish -c Release -o .\publish

docker build -f Dockerfile.published -t <image-name>:local .
```

`Dockerfile.published` は NuGet restore 済みの `publish/` を runtime image にコピーするだけなので、オフライン検証で扱いやすいです。

## 実行順のおすすめ

1. `k8s/namespace.yaml` を apply
2. RabbitMQ の場合だけ `rabbitmq-client-secret` を `app-sample` namespace に作成
3. 各 sample image を build/load
4. `k8s/*.yaml` を apply
5. `kubectl logs` で確認

RabbitMQ secret 作成例:

```powershell
$APP_NS="app-sample"

& $KUBECTL --kubeconfig $KUBECONFIG_PATH create namespace $APP_NS --dry-run=client -o yaml `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -

$RABBITMQ_PASSWORD_B64=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq rabbitmq `
  -o jsonpath="{.data.rabbitmq-password}")

$RABBITMQ_PASSWORD=[System.Text.Encoding]::UTF8.GetString(
  [System.Convert]::FromBase64String($RABBITMQ_PASSWORD_B64)
)

& $KUBECTL --kubeconfig $KUBECONFIG_PATH create secret generic rabbitmq-client-secret `
  -n $APP_NS `
  --from-literal=RABBITMQ_PASSWORD=$RABBITMQ_PASSWORD `
  --dry-run=client -o yaml `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -
```

## Orchestrator samples

追加で以下のサンプルを含めています。

| folder | 内容 |
|---|---|
| `kestra-sample` | Kestra Flow の `PodCreate` から C# コンテナを Kubernetes Pod として実行 |
| `airflow-sample` | Airflow 3.2.2 の `KubernetesPodOperator` から C# コンテナを Kubernetes Pod として実行 |

どちらも `app-sample` namespace に C# Pod を作る前提です。
