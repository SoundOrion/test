# Kestra C# Kubernetes Pod sample

このサンプルは、Kestra Flow から Kubernetes Pod を作成し、C# コンソールアプリのコンテナを実行する例です。

```text
Kestra Flow
  -> PodCreate
      -> app-sample namespace の C# Pod
          -> ログ出力して終了
```

## 前提

- Kestra は `kestra` namespace に起動済み
- C# サンプル Pod は `app-sample` namespace に起動
- C# image 名は `kestra-csharp-job-sample:local`
- Flow は `flows/run-csharp-k8s-pod.yaml`

Kestra 起動 README では、Kestra は `kestra-values2.yaml` と `charts/kestra-1.3.25.tgz` を使い、UI は `svc/kestra` を `8080:8080` に port-forward する構成です。

## 1. 共通変数

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$KUBECTL="C:\Users\Hatsuyama\Desktop\k8s\tools\kubectl\kubectl.exe"
$MINIKUBE="C:\Users\Hatsuyama\Desktop\k8s\tools\minikube\minikube.exe"
$KUBECONFIG_PATH="C:\Users\Hatsuyama\Desktop\k8s\kube\config"

$env:KUBECONFIG=$KUBECONFIG_PATH
```

## 2. namespace 作成

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f .\k8s\namespace.yaml
```

## 3. C# image を build / load

`kestra-sample` ディレクトリで実行します。

```powershell
docker build -t kestra-csharp-job-sample:local .
& $MINIKUBE image load kestra-csharp-job-sample:local --profile=minikube

docker exec minikube docker images | Select-String "kestra-csharp-job-sample"
```

`minikube image load` が profile を見失う場合は、既存 README と同じく `docker save` / `docker cp` / `docker exec minikube docker load` で入れてください。

## 4. Kestra に Pod 作成権限を付ける

Kestra Pod の ServiceAccount 名を取得します。

```powershell
$KESTRA_SA=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pod -n kestra `
  -o jsonpath="{.items[0].spec.serviceAccountName}")

Write-Host $KESTRA_SA
```

RBAC template の `<KESTRA_SERVICE_ACCOUNT>` を置き換えて apply します。

```powershell
(Get-Content .\k8s\kestra-podcreate-rbac-template.yaml) `
  -replace '<KESTRA_SERVICE_ACCOUNT>', $KESTRA_SA `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -
```

## 5. Flow を登録する

Kestra UI で以下を開きます。

```text
http://localhost:8080
```

`Flows` -> `Create Flow` で `flows/run-csharp-k8s-pod.yaml` を貼り付けて保存します。

## 6. 実行する

Flow の `Execute` を押します。

入力例:

```text
message: hello from Kestra and C#
fail: false
steps: 5
```

ログに以下が出れば OK です。

```text
=== Kestra C# Job Sample started ===
step 1/5: processing...
...
=== Kestra C# Job Sample completed ===
```

`fail=true` で実行すると C# アプリが exit code 2 で終了し、Kestra 側でも失敗確認できます。

## 7. Pod を確認する

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n app-sample
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample -l app=kestra-csharp-job-sample --tail=200
```

## 注意

`io.kestra.plugin.kubernetes.core.PodCreate` を使うため、Kestra 側に Kubernetes plugin が入っている必要があります。もし Flow 保存時に task type がないと言われたら、Kestra image / plugin bundle 側に Kubernetes plugin を含めてください。
