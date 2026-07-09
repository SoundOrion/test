# Airflow C# KubernetesPodOperator sample

このサンプルは、Airflow 3.2.2 の DAG から KubernetesPodOperator で C# コンテナを Pod として実行する例です。

```text
Airflow DAG
  -> KubernetesPodOperator
      -> app-sample namespace の C# Pod
          -> ログ出力して終了
```

## 前提

- Airflow は `airflow` namespace に起動済み
- Airflow image は `apache/airflow:3.2.2` 系
- C# サンプル Pod は `app-sample` namespace に起動
- C# image 名は `airflow-csharp-job-sample:local`
- DAG は `dags/run_csharp_k8s_pod.py`

既存 README では、Airflow は `charts/airflow-1.22.0.tgz` と `values/airflow-values.yaml` を使い、UI は `svc/airflow-api-server` を `8081:8080` に port-forward する構成です。

## 1. 共通変数

```powershell
$BASE="C:\Users\Hatsuyama\Desktop\k8s\k8s-offline"
$HELM="C:\Users\Hatsuyama\Desktop\k8s\tools\helm\helm.exe"
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

`airflow-sample` ディレクトリで実行します。

```powershell
docker build -t airflow-csharp-job-sample:local .
& $MINIKUBE image load airflow-csharp-job-sample:local --profile=minikube

docker exec minikube docker images | Select-String "airflow-csharp-job-sample"
```

`minikube image load` が profile を見失う場合は、既存 README と同じく `docker save` / `docker cp` / `docker exec minikube docker load` で入れてください。

## 4. Airflow に Pod 作成権限を付ける

KubernetesPodOperator は Airflow Pod の ServiceAccount で Kubernetes API に Pod 作成リクエストを投げます。

worker Pod の ServiceAccount 名を取得します。

```powershell
$AIRFLOW_SA=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pod -n airflow -l component=worker `
  -o jsonpath="{.items[0].spec.serviceAccountName}")

if ([string]::IsNullOrWhiteSpace($AIRFLOW_SA)) {
  $AIRFLOW_SA=(& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pod -n airflow `
    -o jsonpath="{.items[0].spec.serviceAccountName}")
}

Write-Host $AIRFLOW_SA
```

RBAC template の `<AIRFLOW_SERVICE_ACCOUNT>` を置き換えて apply します。

```powershell
(Get-Content .\k8s\airflow-kubernetespodoperator-rbac-template.yaml) `
  -replace '<AIRFLOW_SERVICE_ACCOUNT>', $AIRFLOW_SA `
  | & $KUBECTL --kubeconfig $KUBECONFIG_PATH apply -f -
```

## 5. DAG 入り Airflow image を作る

Airflow 3.2.2 に合わせて、`airflow-image/Dockerfile` は以下にしています。

```dockerfile
FROM apache/airflow:3.2.2

COPY dags/ ${AIRFLOW_HOME}/dags/
```

`airflow-sample` ディレクトリで実行します。

```powershell
Copy-Item -Recurse -Force .\dags .\airflow-image\dags
cd .\airflow-image

docker build -t airflow-with-dags:3.2.2-csharp-v1 .
& $MINIKUBE image load airflow-with-dags:3.2.2-csharp-v1 --profile=minikube

cd ..
```

Helm に反映します。

```powershell
cd $BASE

& $HELM --kubeconfig $KUBECONFIG_PATH upgrade --install airflow .\charts\airflow-1.22.0.tgz `
  --namespace airflow `
  --reuse-values `
  --set images.airflow.repository=airflow-with-dags `
  --set images.airflow.tag=3.2.2-csharp-v1 `
  --set images.airflow.pullPolicy=Never `
  --timeout 60m
```

## 6. DAG を確認する

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n airflow deploy/airflow-scheduler -- airflow dags list
& $KUBECTL --kubeconfig $KUBECONFIG_PATH exec -n airflow deploy/airflow-scheduler -- airflow dags list-import-errors
```

UI:

```text
http://localhost:8081
```

DAG 一覧に `run_csharp_k8s_pod` が出れば OK です。

## 7. 実行する

Airflow UI で `run_csharp_k8s_pod` を開き、Trigger します。

ログに以下が出れば OK です。

```text
=== Airflow C# Job Sample started ===
step 1/5: processing...
...
=== Airflow C# Job Sample completed ===
```

## 8. Pod を確認する

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n app-sample
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample -l airflow_kpo_in_cluster=True --tail=200
```

label は Airflow / provider のバージョンで変わることがあるため、Pod 名で logs を見る方が確実です。

```powershell
& $KUBECTL --kubeconfig $KUBECONFIG_PATH get pods -n app-sample
& $KUBECTL --kubeconfig $KUBECONFIG_PATH logs -n app-sample <pod-name> --tail=200
```

## 注意

`KubernetesPodOperator` を使うには `apache-airflow-providers-cncf-kubernetes` が Airflow image に入っている必要があります。DAG import error で provider がないと言われた場合は、社内オフライン環境向けに wheel を用意して Airflow image に追加してください。
