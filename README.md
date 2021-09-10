This is a fork of the [original](https://github.com/aidapsibr/aks-prometheus-windows-exporter). This fork contains a version that has been customised for the way we interact with Azure and AKS.

---

# Prometheus Windows Exporter in Windows AKS Node pools
Metrics on Kubernetes are quite streamlined with a typical prometheus and grafana installation, but if you need to run Windows in your clusters for any reason things don't go so smoothly. This will be a bit of a how-to guide on getting some metrics you can start to build dashboards with or setup alerts, etc.

## Why doesn't this work out of the box?
Windows in Kubernetes at present runs in `Process isolation` (which is good for metrics!), but doesn't allow `privileged` containers (bad!). This is why so few monitoring solutions support Windows and even if they do, it's relatively limited to what it can gather. The typical mechanism for gathering metrics is with a `daemonset` so that one `pod` will be placed on each `node`. This `pod` would gather all the metrics for it's assigned `node`, but again this doesn't really work on Windows.

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

The extension can be installed in your cluster via a Terraform module similar to the below (Octopus staff can look in the Nautilus repo for our actual usage). You'll need to provide a `data` object that obtains the VMSS object in your cluster (see the `depends_on` below).

```yaml
resource "azurerm_virtual_machine_scale_set_extension" "blue_windows_exporter" {
  depends_on = [
    data.azurerm_virtual_machine_scale_set.blue_bldwin
  ]
  name                         = "windows-exporter-dsc"
  virtual_machine_scale_set_id = data.azurerm_virtual_machine_scale_set.blue_bldwin.id
  publisher                    = "Microsoft.Powershell"
  type                         = "DSC"
  type_handler_version         = "2.80"
  # ensure that the AKS custom script extension has already run
  provision_after_extensions = ["vmssCSE"]
  auto_upgrade_minor_version = false
  settings = jsonencode({
    wmfVersion = "latest"
    configuration = {
      url      = var.vmss_metrics_extension_zip
      script   = "aks_setup"
      function = "Setup"
    }
    privacy = {
      dataEnabled = "Disable"
    }
  })
}
```
`var.vmss_metrics_extension_zip` should be a variable pointing to a downloadable ZIP of the contents of the `aks_setup` folder in this repo, with the `windows-exporter` .msi included. This repo has a GitHub Action set up to package the zip as a release on the repo. If you're using this in production, we recommend packaging it yourself.

The `aks_setup.psm1` file is a DSC module which installs the `windows-exporter` service on port 9100 and configures it with a number of metrics. If you need more or less metrics, you can change the options in that file.

## Scraping the metrics

To mimic the Daemonset approach used for Linux nodes, we provide a Dockerfile in the `nginx` folder which will create a Windows container that forwards to an IP defined in environment variables for the container. To use this in your cluster, you'll want to set up a Daemonset similar to this:

```yaml
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: # a useful name
  namespace: # your monitoring namespace
  labels:
    # labels that match any existing Prometheus ServiceMonitors
spec:
  selector:
    matchLabels:
      # labels that match any existing Prometheus ServiceMonitors
  updateStrategy:
    type: RollingUpdate
  template:
    metadata:
      labels:
        # labels that match any existing Prometheus ServiceMonitors
    spec:
      hostNetwork: false
      containers:
        - name: windows-metric-proxy
          image: # your docker container location
          imagePullPolicy: Always
          ports:
            - name: metrics
              containerPort: 9100
              protocol: TCP
          env:
            - name: PROXY_HOSTIP
              # this will get the current node's internal IP and forward metric scrapes to the windows-exporter service running on the node
              valueFrom:
                fieldRef:
                  fieldPath: status.hostIP
            - name: PROXY_PORT
              value: '9100'
      securityContext:
        runAsNonRoot: false
      nodeSelector:
        kubernetes.io/os: windows
```

Depending on your metric pipeline, you may need to to further configuration to ensure you're capturing `windows_*` metric targets, as this is what the `windows-exporter` exposes.