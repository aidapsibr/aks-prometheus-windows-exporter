# installs powershell dsc on all windows vmss
function Deploy-PrometheusWindowsExporter([string]$subscription, [string]$resourceGroup)
{
    $ErrorActionPreference = "Stop";
    $PSDefaultParameterValues['*:ErrorAction']='Stop';

    write "";
    write "Checking $subscription/$resourceGroup for windows vmss that need powershell dsc installed";

    $scaleSets = az vmss list --subscription $subscription --resource-group $resourceGroup | ConvertFrom-Json;

    # grab all the windows vmss in the rg
    # install only on vmss that don't have powershell dsc installed unless $forceUpdate is true
    $vmssNamesToRun = $scaleSets.Where({
        $_.virtualMachineProfile.osProfile.linuxConfiguration -eq $null `
        -and ($forceUpdate -or $_.virtualMachineProfile.extensionProfile.extensions.where({$_.name -eq "Microsoft.Powershell.DSC"}).Count -eq 0)}) | `
        % Name;

    if($vmssNamesToRun.Length -gt 0)
    {
        write "";
        write "Installing DSC on the following VMSS:";
        write $vmssNamesToRun;

        foreach($vmssName in $vmssNamesToRun)
        {
            write "";

            write "Installing DSC on vmss $vmssName...";
            $installResult = az vmss extension set `
                --extension-instance-name "Microsoft.Powershell.DSC" `
                --name "DSC" `
                --publisher "Microsoft.Powershell" `
                --version "2.80" `
                --subscription $subscription `
                --resource-group $resourceGroup `
                --vmss-name $vmssName `
                --provision-after-extensions "vmssCSE" `
                --settings '{\"wmfVersion\":\"latest\", \"configuration\":{\"url\":\"https://github.com/aidapsibr/aks-prometheus-windows-exporter/files/6488224/aks_setup.zip\", \"script\":\"aks_setup.ps1\", \"function\":\"Setup\"}}' `
                --force-update;
        
            write "Updating instances on vmss $vmssName...";
            $updateResult = az vmss update-instances `
                --subscription $subscription `
                --resource-group $resourceGroup `
                --name $vmssName `
                --instance-ids *; 

            write "DSC installation complete on vmss $vmssName";
        }
    }

    write "";
    write "All windows vmss have powershell dsc extension installed in $subscription/$resourceGroup";
}
