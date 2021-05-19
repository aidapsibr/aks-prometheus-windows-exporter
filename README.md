# Prometheus Windows Exporter in Windows AKS Node pools
Metrics on Kubernetes are quite streamlined with a typical prometheus and grafana installation, but if you need to run Windows in your clusters for any reason things don't go so smoothly. This will be a bit of a how-to guide on getting some metrics you can start to build dashboards with or setup alerts, etc.

## Why doesn't this work out of the box?
Windows in Kuberenetes at present runs in `Process isolation` (which is good for metrics!), but doesn't allow `priveleged` containers (bad!). This is why so few monitoring solutions support Windows and even if they do, it's relatively limited to what it can gather. The typical mechanism for gathering metrics is with a `daemonset` so that one `pod` will be placed on each `node`. This `pod` would gather all the metrics for it's assigned `node`, but again this doesn't really work on Windows.

## What *can* we do?
Well, we can do a lot actually, but we have to piece it together ourselves for the time being. Cloud providers, in this case Azure, build on top of IaaS primitives like Virtual Machine Scale Sets. We can directly interact with those to install some software on each host `node`. That along with a way to publicize the nodes' scraping port to Prometheus is all we need, really.

## How do we install software?
There are a few ways to install software on a VM scale set in Azure. The most common are:

- custom image
- Custom Script Extension (CSE)
- PowerShell DSC
- other provisioners such as Chef 

Custom image is a rather cumbersome process of maintaining the image where as AKS typically provides base OS updates, so this wouldn't be ideal. CSE is actually perfect, so perfect that this is how AKS delivers its software stack (kubelet, nssm, azure CNI), *but* a vm scale set can only have *one* CSE, DANG! 

We opted for PowerShell DSC since we use PowerShell heavily anyway and have no experience with other provisioners.

## Prerequites

- AKS cluster with Windows nodes
- Prometheus *[and optionally Grafana]*
- Contributor access to the resources
- Deployment permissions to the cluster
- HELM CLI

## Getting started

You will want to clone this repo so that you can run the install script and have the helm chart since I haven't publihsed it anywhere yet. I have however pushed the docker image to docker hub, so you wont have to build anything unless you want to do so.

## Running the installer

Start by running [install.ps1](https://github.com/aidapsibr/aks-prometheus-windows-exporter/blob/main/install.ps1), this will add a function that you can call from PowerShell: `Deploy-PrometheusWindowsExporter`.

Then you can run it for a particular cluster. 
> The resource group should be the node resource group, not the resource group the cluster resource is in.
```powershell
Deploy-PrometheusWindowsExporter -subscription "" -resourceGroup "";
```
## Deploying the Prometheus endpoint publisher
At this point, all of our Windows nodes can gather stats and are listening on 9100, but Prometheus doesn't know that... We ended up building a small app and deployment that just identitifies all of the windows nodes and lets Prometheus know about them. In the following snippet, we install the chart in our cluster. We recommend creating a namespace or using the namespace that Prometheus is in (monitoring for us).

```bash
helm install windows-prometheus-sync ./WindowsPrometheusSync/helm/windows-prometheus-sync/ --namespace monitoring
```

Sample node usage table built in Grafana from this process: https://github.com/aidapsibr/aks-prometheus-windows-exporter/blob/main/grafana-windows-server-table-panel.json
![image](https://user-images.githubusercontent.com/621605/118737301-6d189780-b7f9-11eb-96f3-a77269a7d4ea.png)
