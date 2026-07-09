# C# から k8s / minikube 上の RabbitMQ・Kafka・NATS に接続する README

この README は、**RabbitMQ / Kafka / NATS が minikube 上の Kubernetes に Helm で起動済み**で、  
**C# アプリも Kubernetes Pod として minikube 上で動かす**前提の接続手順です。

ローカル PC から `localhost` に接続する説明ではなく、Pod 内から Kubernetes Service DNS を使って接続します。

---

## 目次

1. 前提
2. 接続先一覧
3. 起動確認
4. C# サンプルプロジェクト作成
5. C# サンプルコード
6. Docker image 作成
7. minikube へ image を取り込む
8. RabbitMQ password Secret をアプリ namespace に作る
9. Kubernetes Deployment
10. 実行・ログ確認
11. backend ごとの動作確認
12. Kafka 接続時の注意
13. トラブルシュート
14. 参考

---

## 1. 前提

この README では、以下の namespace / Service を前提にします。

| backend | namespace | Service 名 | port |
|---|---:|---:|---:|
| RabbitMQ | `rabbitmq` | `rabbitmq` | `5672` |
| Kafka | `kafka` | `kafka` または `kafka-controller` | `9092` |
| NATS | `nats` | `nats` | `4222` |

C# アプリ用 namespace は `app-sample` とします。

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$KUBECTL="C:\Users\Hatsuyama\Desktop\k8s\tools\kubectl\kubectl.exe"
$MINIKUBE="C:\Users\Hatsuyama\Desktop\k8s\tools\minikube\minikube.exe"
$KUBECONFIG_PATH="C:\Users\Hatsuyama\Desktop\k8s\kube\config"

$env:KUBECONFIG=$KUBECONFIG_PATH

cd $BASE
```

---

## 2. 接続先一覧

### C# アプリを k8s / minikube 内の Pod として動かす場合

| backend | 接続先 |
|---|---|
| RabbitMQ | `rabbitmq.rabbitmq.svc.cluster.local:5672` |
| Kafka | `kafka.kafka.svc.cluster.local:9092` |
| Kafka, Service名が `kafka-controller` の場合 | `kafka-controller.kafka.svc.cluster.local:9092` |
| NATS | `nats://nats.nats.svc.cluster.local:4222` |

Kubernetes Service DNS は基本的に以下の形です。

```text
<service-name>.<namespace>.svc.cluster.local
```

Service 名は環境によって違う場合があるので、必ず確認してください。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n nats
```

---

## 3. 起動確認

まず backend 側の Pod と Service が見えることを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc  -n rabbitmq

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n kafka
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc  -n kafka

& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc  -n nats
```

期待値の例です。

```text
rabbitmq-0             1/1   Running
kafka-controller-0     1/1   Running
nats-0                 1/1   Running
```

---

## 4. C# サンプルプロジェクト作成

作業用ディレクトリを作ります。

```powershell
mkdir csharp-broker-sample
cd csharp-broker-sample

dotnet new console -n MessageBrokerSample
cd MessageBrokerSample
```

NuGet package を追加します。

```powershell
dotnet add package RabbitMQ.Client
dotnet add package Confluent.Kafka
dotnet add package NATS.Net
```

オフライン環境の場合は、社内 NuGet feed または事前に取得済みの NuGet package を使って restore してください。

---

## 5. C# サンプルコード

`Program.cs` を以下に置き換えます。

このサンプルは、環境変数 `MSG_MODE` で動作を切り替えます。

| `MSG_MODE` | 動作 |
|---|---|
| `all-publish-loop` | RabbitMQ / Kafka / NATS に定期 publish |
| `rabbitmq-publish` | RabbitMQ に 1 件 publish |
| `rabbitmq-consume` | RabbitMQ queue を consume |
| `kafka-produce` | Kafka topic に 1 件 produce |
| `kafka-consume` | Kafka topic を consume |
| `nats-publish` | NATS subject に 1 件 publish |
| `nats-subscribe` | NATS subject を subscribe |

```csharp
using System.Text;
using Confluent.Kafka;
using NATS.Client.Core;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var mode = Env("MSG_MODE", "all-publish-loop").ToLowerInvariant();

Console.WriteLine($"MSG_MODE={mode}");

try
{
    switch (mode)
    {
        case "all-publish-loop":
            await RunAllPublishLoopAsync(cts.Token);
            break;

        case "rabbitmq-publish":
            await RabbitMqPublishAsync(cts.Token);
            break;

        case "rabbitmq-consume":
            await RabbitMqConsumeAsync(cts.Token);
            break;

        case "kafka-produce":
            await KafkaProduceAsync(cts.Token);
            break;

        case "kafka-consume":
            await KafkaConsumeAsync(cts.Token);
            break;

        case "nats-publish":
            await NatsPublishAsync(cts.Token);
            break;

        case "nats-subscribe":
            await NatsSubscribeAsync(cts.Token);
            break;

        default:
            throw new InvalidOperationException($"Unknown MSG_MODE: {mode}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Canceled.");
}

static async Task RunAllPublishLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await RabbitMqPublishAsync(ct);
        await KafkaProduceAsync(ct);
        await NatsPublishAsync(ct);

        Console.WriteLine("Published to all backends.");
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
    }
}

static async Task RabbitMqPublishAsync(CancellationToken ct)
{
    var host = Env("RABBITMQ_HOST", "rabbitmq.rabbitmq.svc.cluster.local");
    var port = int.Parse(Env("RABBITMQ_PORT", "5672"));
    var user = Env("RABBITMQ_USER", "user");
    var password = Env("RABBITMQ_PASSWORD", "");
    var vhost = Env("RABBITMQ_VHOST", "/");
    var queue = Env("RABBITMQ_QUEUE", "test-queue");

    var factory = new ConnectionFactory
    {
        HostName = host,
        Port = port,
        UserName = user,
        Password = password,
        VirtualHost = vhost
    };

    await using var connection = await factory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    await channel.QueueDeclareAsync(
        queue: queue,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null
    );

    var message = $"hello from C# RabbitMQ at {DateTimeOffset.Now:O}";
    var body = Encoding.UTF8.GetBytes(message);

    await channel.BasicPublishAsync(
        exchange: "",
        routingKey: queue,
        body: body
    );

    Console.WriteLine($"RabbitMQ published: queue={queue}, message={message}");
}

static async Task RabbitMqConsumeAsync(CancellationToken ct)
{
    var host = Env("RABBITMQ_HOST", "rabbitmq.rabbitmq.svc.cluster.local");
    var port = int.Parse(Env("RABBITMQ_PORT", "5672"));
    var user = Env("RABBITMQ_USER", "user");
    var password = Env("RABBITMQ_PASSWORD", "");
    var vhost = Env("RABBITMQ_VHOST", "/");
    var queue = Env("RABBITMQ_QUEUE", "test-queue");

    var factory = new ConnectionFactory
    {
        HostName = host,
        Port = port,
        UserName = user,
        Password = password,
        VirtualHost = vhost
    };

    await using var connection = await factory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    await channel.QueueDeclareAsync(
        queue: queue,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null
    );

    var consumer = new AsyncEventingBasicConsumer(channel);

    consumer.ReceivedAsync += async (_, ea) =>
    {
        var message = Encoding.UTF8.GetString(ea.Body.ToArray());
        Console.WriteLine($"RabbitMQ received: queue={queue}, message={message}");

        await channel.BasicAckAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false
        );
    };

    await channel.BasicConsumeAsync(
        queue: queue,
        autoAck: false,
        consumer: consumer
    );

    Console.WriteLine($"RabbitMQ consuming: queue={queue}");
    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
}

static async Task KafkaProduceAsync(CancellationToken ct)
{
    var bootstrapServers = Env("KAFKA_BOOTSTRAP_SERVERS", "kafka.kafka.svc.cluster.local:9092");
    var topic = Env("KAFKA_TOPIC", "test-topic");

    var config = new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        ClientId = "csharp-minikube-sample"
    };

    using var producer = new ProducerBuilder<string, string>(config).Build();

    var message = $"hello from C# Kafka at {DateTimeOffset.Now:O}";

    var result = await producer.ProduceAsync(
        topic,
        new Message<string, string>
        {
            Key = Environment.MachineName,
            Value = message
        },
        ct
    );

    Console.WriteLine($"Kafka produced: {result.TopicPartitionOffset}, message={message}");
}

static Task KafkaConsumeAsync(CancellationToken ct)
{
    var bootstrapServers = Env("KAFKA_BOOTSTRAP_SERVERS", "kafka.kafka.svc.cluster.local:9092");
    var topic = Env("KAFKA_TOPIC", "test-topic");
    var groupId = Env("KAFKA_GROUP_ID", "csharp-minikube-sample");

    var config = new ConsumerConfig
    {
        BootstrapServers = bootstrapServers,
        GroupId = groupId,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = true
    };

    using var consumer = new ConsumerBuilder<string, string>(config).Build();

    consumer.Subscribe(topic);

    Console.WriteLine($"Kafka consuming: topic={topic}, groupId={groupId}");

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var cr = consumer.Consume(ct);
            Console.WriteLine($"Kafka received: {cr.TopicPartitionOffset}, key={cr.Message.Key}, value={cr.Message.Value}");
        }
    }
    finally
    {
        consumer.Close();
    }

    return Task.CompletedTask;
}

static async Task NatsPublishAsync(CancellationToken ct)
{
    var url = Env("NATS_URL", "nats://nats.nats.svc.cluster.local:4222");
    var subject = Env("NATS_SUBJECT", "test.subject");

    await using var nats = new NatsClient(new NatsOpts
    {
        Url = url
    });

    var message = $"hello from C# NATS at {DateTimeOffset.Now:O}";

    await nats.PublishAsync<string>(
        subject: subject,
        data: message,
        cancellationToken: ct
    );

    Console.WriteLine($"NATS published: subject={subject}, message={message}");
}

static async Task NatsSubscribeAsync(CancellationToken ct)
{
    var url = Env("NATS_URL", "nats://nats.nats.svc.cluster.local:4222");
    var subject = Env("NATS_SUBJECT", "test.subject");

    await using var nats = new NatsClient(new NatsOpts
    {
        Url = url
    });

    Console.WriteLine($"NATS subscribing: subject={subject}");

    await foreach (var msg in nats.SubscribeAsync<string>(subject).WithCancellation(ct))
    {
        Console.WriteLine($"NATS received: subject={msg.Subject}, data={msg.Data}");
    }
}

static string Env(string name, string fallback)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}
```

---

## 6. Docker image 作成

`MessageBrokerSample` ディレクトリに `Dockerfile` を作成します。

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MessageBrokerSample.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MessageBrokerSample.dll"]
```

image を build します。

```powershell
docker build -t csharp-broker-sample:local .
```

---

## 7. minikube へ image を取り込む

### 方法A: `minikube image load` が使える場合

```powershell
& $MINIKUBE image load csharp-broker-sample:local --profile=minikube
```

確認します。

```powershell
docker exec minikube docker images | Select-String "csharp-broker-sample"
```

### 方法B: Docker Desktop から minikube node に直接 load する場合

`minikube image load` が profile を見失う場合はこちらを使います。

```powershell
mkdir "$BASE\images" -Force | Out-Null

docker save csharp-broker-sample:local -o "$BASE\images\csharp-broker-sample_local.tar"

docker cp "$BASE\images\csharp-broker-sample_local.tar" "minikube:/home/docker/csharp-broker-sample_local.tar"

docker exec minikube docker load -i "/home/docker/csharp-broker-sample_local.tar"

docker exec minikube rm "/home/docker/csharp-broker-sample_local.tar"

Remove-Item "$BASE\images\csharp-broker-sample_local.tar"
```

確認します。

```powershell
docker exec minikube docker images | Select-String "csharp-broker-sample"
```

---

## 8. RabbitMQ password Secret をアプリ namespace に作る

重要ポイントです。

Kubernetes の `secretKeyRef` は、**同じ namespace の Secret しか参照できません**。  
RabbitMQ の Secret は通常 `rabbitmq` namespace にあるため、C# アプリを `app-sample` namespace に置く場合は、アプリ用 namespace に Secret を作ります。

```powershell
$APP_NS="app-sample"

& $KUBECTL --kubeconfig $KUBECONFIG_PATH create namespace $APP_NS --dry-run=client -o yaml `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -
```

RabbitMQ の password を取得します。

```powershell
$RABBITMQ_PASSWORD_B64=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq rabbitmq `
  -o jsonpath="{.data.rabbitmq-password}")

$RABBITMQ_PASSWORD=[System.Text.Encoding]::UTF8.GetString(
  [System.Convert]::FromBase64String($RABBITMQ_PASSWORD_B64)
)

Write-Host $RABBITMQ_PASSWORD
```

アプリ namespace に Secret を作ります。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH create secret generic rabbitmq-client-secret `
  -n $APP_NS `
  --from-literal=RABBITMQ_PASSWORD=$RABBITMQ_PASSWORD `
  --dry-run=client -o yaml `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -
```

Secret 名が `rabbitmq` ではない場合は、以下で確認して読み替えてください。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq
```

---

## 9. Kubernetes Deployment

`k8s-csharp-broker-sample.yaml` を作成します。

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: csharp-broker-sample
  namespace: app-sample
spec:
  replicas: 1
  selector:
    matchLabels:
      app: csharp-broker-sample
  template:
    metadata:
      labels:
        app: csharp-broker-sample
    spec:
      containers:
        - name: app
          image: csharp-broker-sample:local
          imagePullPolicy: IfNotPresent
          env:
            # mode:
            #   all-publish-loop
            #   rabbitmq-publish
            #   rabbitmq-consume
            #   kafka-produce
            #   kafka-consume
            #   nats-publish
            #   nats-subscribe
            - name: MSG_MODE
              value: all-publish-loop

            # RabbitMQ
            - name: RABBITMQ_HOST
              value: rabbitmq.rabbitmq.svc.cluster.local
            - name: RABBITMQ_PORT
              value: "5672"
            - name: RABBITMQ_USER
              value: user
            - name: RABBITMQ_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: rabbitmq-client-secret
                  key: RABBITMQ_PASSWORD
            - name: RABBITMQ_VHOST
              value: "/"
            - name: RABBITMQ_QUEUE
              value: test-queue

            # Kafka
            # Service 名が kafka-controller の場合は:
            # kafka-controller.kafka.svc.cluster.local:9092
            - name: KAFKA_BOOTSTRAP_SERVERS
              value: kafka.kafka.svc.cluster.local:9092
            - name: KAFKA_TOPIC
              value: test-topic
            - name: KAFKA_GROUP_ID
              value: csharp-minikube-sample

            # NATS
            - name: NATS_URL
              value: nats://nats.nats.svc.cluster.local:4222
            - name: NATS_SUBJECT
              value: test.subject
```

apply します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f .\k8s-csharp-broker-sample.yaml
```

---

## 10. 実行・ログ確認

Pod を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n app-sample
```

ログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample deploy/csharp-broker-sample -f
```

`MSG_MODE=all-publish-loop` の場合、以下のようなログが出れば OK です。

```text
MSG_MODE=all-publish-loop
RabbitMQ published: queue=test-queue, message=hello from C# RabbitMQ ...
Kafka produced: test-topic[0]@...
NATS published: subject=test.subject, message=hello from C# NATS ...
Published to all backends.
```

---

## 11. backend ごとの動作確認

### 11.1 RabbitMQ consume に切り替える

Deployment の環境変数を変更します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=rabbitmq-consume
```

ログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample deploy/csharp-broker-sample -f
```

別途 RabbitMQ に publish したい場合は、再度 `rabbitmq-publish` に切り替えます。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=rabbitmq-publish
```

producer は 1 件 publish して終了するため、Deployment では再起動される可能性があります。  
継続的に確認したい場合は `all-publish-loop` を使うのが簡単です。

---

### 11.2 Kafka produce / consume に切り替える

Kafka produce:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=kafka-produce
```

Kafka consume:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=kafka-consume
```

ログ:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample deploy/csharp-broker-sample -f
```

Kafka の Service 名が `kafka-controller` の場合は、環境変数を変えます。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  KAFKA_BOOTSTRAP_SERVERS=kafka-controller.kafka.svc.cluster.local:9092
```

---

### 11.3 NATS publish / subscribe に切り替える

NATS publish:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=nats-publish
```

NATS subscribe:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  MSG_MODE=nats-subscribe
```

ログ:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample deploy/csharp-broker-sample -f
```

---

## 12. Kafka 接続時の注意

Kafka は RabbitMQ / NATS より接続でハマりやすいです。

理由は、Kafka client が最初に `bootstrap.servers` へ接続したあと、broker から返される `advertised.listeners` のアドレスへ接続し直すためです。

そのため、ローカル PC から `kubectl port-forward -n kafka svc/kafka 9092:9092` しても、broker が `kafka.kafka.svc.cluster.local:9092` や Pod DNS を返す設定だと、ローカル PC の C# client は接続できないことがあります。

この README では、C# アプリも k8s 内に置いているため、以下を使います。

```text
kafka.kafka.svc.cluster.local:9092
```

または:

```text
kafka-controller.kafka.svc.cluster.local:9092
```

まずは in-cluster 接続で動作確認するのが安全です。

---

## 13. トラブルシュート

### 13.1 Pod が `ImagePullBackOff` になる

minikube node に C# アプリ image が入っていない可能性があります。

```powershell
docker exec minikube docker images | Select-String "csharp-broker-sample"
```

入っていなければ、もう一度 image を load してください。

```powershell
& $MINIKUBE image load csharp-broker-sample:local --profile=minikube
```

または Docker Desktop から `docker save` / `docker cp` / `docker load` してください。

Deployment 側の `imagePullPolicy` は `IfNotPresent` にします。

```yaml
imagePullPolicy: IfNotPresent
```

---

### 13.2 RabbitMQ に接続できない

確認ポイント:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n rabbitmq
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample deploy/csharp-broker-sample --tail=200
```

よくある原因:

```text
- RABBITMQ_HOST が Service 名と違う
- RABBITMQ_PASSWORD が空
- Secret 名が rabbitmq ではない
- app-sample namespace に rabbitmq-client-secret を作っていない
- vhost が違う
```

アプリ namespace の Secret を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get secret -n app-sample rabbitmq-client-secret
```

---

### 13.3 Kafka に接続できない

Service 名を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc -n kafka
```

アプリの `KAFKA_BOOTSTRAP_SERVERS` を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe deployment -n app-sample csharp-broker-sample
```

`kafka` Service がない場合は `kafka-controller` に変更します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH set env deployment/csharp-broker-sample `
  -n app-sample `
  KAFKA_BOOTSTRAP_SERVERS=kafka-controller.kafka.svc.cluster.local:9092
```

Kafka Pod のログも確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n kafka <kafka-pod-name> --tail=200
```

---

### 13.4 NATS に接続できない

Service と Pod を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n nats
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get svc  -n nats
```

アプリの `NATS_URL` を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH describe deployment -n app-sample csharp-broker-sample
```

NATS Pod のログを確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n nats nats-0 --tail=200
```

---

### 13.5 Service DNS が解決できるか確認したい

一時 Pod から DNS を確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH run dns-test `
  -n app-sample `
  --restart=Never `
  --image=busybox:1.36 `
  --command -- sleep 3600
```

確認します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n app-sample dns-test -- `
  nslookup rabbitmq.rabbitmq.svc.cluster.local

& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n app-sample dns-test -- `
  nslookup kafka.kafka.svc.cluster.local

& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n app-sample dns-test -- `
  nslookup nats.nats.svc.cluster.local
```

不要になったら削除します。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH delete pod -n app-sample dns-test
```

busybox image がオフライン環境の minikube にない場合は、既に入っている別 image で代用してください。

---

### 13.6 C# Pod の中から TCP 接続を確認したい

.NET runtime image には `curl` や `nc` が入っていないことがあります。  
その場合は、確認用の debug image を別途用意して使います。

例:

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH run net-debug `
  -n app-sample `
  --restart=Never `
  --image=<offline環境に入っているdebug用image> `
  --command -- sleep 3600
```

確認例:

```powershell
# RabbitMQ
nc -vz rabbitmq.rabbitmq.svc.cluster.local 5672

# Kafka
nc -vz kafka.kafka.svc.cluster.local 9092

# NATS
nc -vz nats.nats.svc.cluster.local 4222
```

---

## 14. 参考

### backend 側 README の要点

- RabbitMQ の k8s 内接続先例: `rabbitmq.rabbitmq.svc.cluster.local:5672`
- RabbitMQ のローカル PC 向け接続は `kubectl port-forward -n rabbitmq svc/rabbitmq 5672:5672`
- Kafka の k8s 内接続先例: `kafka.kafka.svc.cluster.local:9092` または `kafka-controller.kafka.svc.cluster.local:9092`
- Kafka はローカル PC から port-forward する場合、`advertised.listeners` の設定に注意
- NATS の k8s 内接続先例: `nats://nats.nats.svc.cluster.local:4222`

### C# client

- RabbitMQ .NET/C# Client API Guide  
  https://www.rabbitmq.com/client-libraries/dotnet-api-guide

- Confluent Kafka .NET client  
  https://docs.confluent.io/kafka-clients/dotnet/current/overview.html

- Kafka producer / consumer configuration  
  https://docs.confluent.io/platform/current/installation/configuration/producer-configs.html  
  https://docs.confluent.io/platform/current/installation/configuration/consumer-configs.html

- NATS .NET API docs  
  https://nats-io.github.io/nats.net/api/NATS.Client.Core.NatsOpts.Url.html

---

## 最短まとめ

C# アプリを minikube 内の Pod として動かすなら、接続先は以下です。

```text
RabbitMQ:
  rabbitmq.rabbitmq.svc.cluster.local:5672

Kafka:
  kafka.kafka.svc.cluster.local:9092
  または
  kafka-controller.kafka.svc.cluster.local:9092

NATS:
  nats://nats.nats.svc.cluster.local:4222
```

ローカル PC から `localhost` で見るより、まずは **C# アプリも k8s 内に置いて Service DNS でつなぐ**のが一番安定します。
