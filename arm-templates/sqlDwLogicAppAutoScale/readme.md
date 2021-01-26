
This sample includes 3 templates to showcase different scenarios for using Logic apps to autoscale compute resources for a SQL Data Warehouse

## Scenario 1: Scheduled autoscaling for a single data warehouse

![scheduled autoscale](media/scenario1-scale-overview.png)

In this scenario, you deploy a timer-based Logic app configured with a [service managed identity that is used to authenticate to SQL Data Warehouse](https://docs.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview). The Logic app issues scale commands to the data warehouse based on the schedule configured during deployment. 

**1. Deploy timeschedule-based autoscale template**

> [!NOTE]
> You will need an existing SQL Data Warehouse to deploy the template below. 

* Use the server-name and database-name properties of your data warehouse, and an autoscale schedule to complete the deployment. 
* You can additionally specify the timezone for the autoscale schedule, and if the schedule should only run on weekdays.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fsqldwexternaldata.blob.core.windows.net%2Farmtemplates%2FScheduledAutoScaleDW.json" target="_blank">
<img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazure.png"/>
</a>

**2. Configure database access permissions for logic app**

> [!NOTE]
>
> To successfully complete this step, your account will need 'Owner' permissions on your data warehouse, or the permission to assign roles to other users

Your logic app will need permissions to access your data warehouse. You will provide this using the Azure Cloud Shell:

1. [Launch the Azure Cloud Shell](https://shell.azure.com/) 
2. Configure the shell for a Bash scripting environment
	![cloud shell bash environment](media/azurecloudshell-bash.png)

3. Set the active subscription to the subscription containing your data warehouse

	```
	az account set --subscription <Subscription ID>
	```

4. Configure 'Contributor' access for your logic app using the command below.
	* Substitute the relevant parameters for your deployment in the *SubscriptionId*, *ResourceGroupName*, *ServerName*, and *DatabaseName* variables below
	* You can find the 'ObjectId' identity for your logic app in the 'Identity' section of the logic app settings blade. 
	![get logic app objectid](media/logicapp-managedidentity.png)


	```
	az role assignment create --assignee-object-id <LogicApp ObjectId> --role Contributor --scope "/subscriptions/<subscriptionId>/resourceGroups/<resourceGroupName>/providers/Microsoft.Sql/servers/<serverName>/databases/<databaseName>"
	```

Your Logic app is now configured to automatically scale your data warehouse at the times you scheduled.

**3. Optionally modify scale schedule**

If you would like to modify the autoscale schedule configured during deployment, navigate to the Logic app and select the 'Initialize API variables' activity. Expand the activity to reveal the variables used for orchestration. 

* Modify the 'ScaleUpTime' key to change the time the data warehouse is scaled up
* Modify the 'ScaleUpSLO' key to change the SLO the data warehouse is scaled up to
* Modify the 'ScaleDownTime' key to change the time the data warehouse is scaled down
* Modify the 'ScaleDownSLO' key to change the SLO the data warehouse is scaled down to
* Make sure to update the 'Recurrence' activity to reflect the changes in 'ScaleUpTime', or 'ScaleDownTime'

![modify autoscale schedule](media/scenario1-modifyvariables.png)

## Scenario 2: Event-based autoscaling for a single data warehouse

![event-based autoscale](media/scenario2-scale-overview.png)

In this scenario, you deploy an event-based Logic app configured with a [service managed identity that is used to authenticate to SQL Data Warehouse](https://docs.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview). The Logic app uses Azure alerts to determine when to scale up and scale down a SQL Data Warehouse. 

**1. Deploy event-based autoscale template**

> [!NOTE]
> You will need an existing SQL Data Warehouse to deploy the template below.

* The template below deploys a Logic app, and two metric alerts to trigger scale up and scale down activities.
* Use the server-name, database-name properties of your data warehouse, and scale cap limits to complete the deployment. 

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fsqldwexternaldata.blob.core.windows.net%2Farmtemplates%2FEventbasedAutoScaleDW.json" target="_blank">
<img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazure.png"/>
</a>

**2. Configure database access permissions for logic app**

> [!NOTE]
>
> To successfully complete this step, your account will need 'Owner' permissions on your data warehouse, or the permission to assign roles to other users

Your logic app will need permissions to access your data warehouse. You will provide this using the Azure Cloud Shell:

1. [Launch the Azure Cloud Shell](https://shell.azure.com/) 
2. Configure the shell for a Bash scripting environment
	![cloud shell bash environment](media/azurecloudshell-bash.png)

3. Set the active subscription to the subscription containing your data warehouse

	```
	az account set --subscription <Subscription ID>
	```

4. Configure 'Contributor' access for your logic app using the command below.
	* Substitute the relevant parameters for your deployment in the *SubscriptionId*, *ResourceGroupName*, *ServerName*, and *DatabaseName* variables below
	* You can find the 'ObjectId' identity for your logic app in the 'Identity' section of the logic app settings blade. 
	![get logic app objectid](media/logicapp-managedidentity.png)


	```
	az role assignment create --assignee-object-id <LogicApp ObjectId> --role Contributor --scope "/subscriptions/<subscriptionId>/resourceGroups/<resourceGroupName>/providers/Microsoft.Sql/servers/<serverName>/databases/<databaseName>"
	```

Your Logic app is now configured to autoscale your data warehouse based on resource usage patterns.

**3. Optionally modify alert triggers**

The default alert triggers are set to:

* Scale up the data warehouse (up to configured max SLO) if average CPU usage >= 80% for the last 15 minutes
* Scale down the data warehouse (down to configured min SLO) if average CPU usage < 40% for the last 60 minutes 

If you would like to modify these settings, navigate to your data warehouse resource group, and select the 'Alerts' settings. Click the 'Manage alert rules' button to manage the existing alert rules - these alerts are configured with 'dwScaleUpAlert' or 'dwScaleDownAlert' prefixes.

![modify alert rules](media/scenario2-manage-alertrules.png)


## Scenario 3: Scheduled autoscaling for a fleet of data warehouses

![scheduled autoscale for dwfleet](media/scenario3-autoscale-overview.png)

In this scenario, you deploy a timer-based Logic app that uses a service principal to autoscale a fleet of data warehouses. The Logic app uses Azure tags to identify both candidate data warehouses and their autoscale schedules. The app also uses a [user-managed Azure service principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals) to authenticate to the various data warehouses and issue scale commands. 

**1. Create a service principal and configure access permissions**

Your logic app will need permissions to access all data warehouses you intend to autoscale. You will configure this using the Azure Cloud Shell:

> [!NOTE]
>
> To successfully complete this step, your account will need to have permission to create a service principal

1. [Launch the Azure Cloud Shell](https://shell.azure.com/) 
2. Configure the shell for a Bash scripting environment
	![cloud shell bash environment](media/azurecloudshell-bash.png)

3. Set the active subscription to the subscription containing your data warehouse

	```
	az account set --subscription <Subscription ID>
	```

4. Create a service principal and configure access using the command below.
	* Enter a service principal name for the *ServicePrincipalName* parameter below
	* Make sure to copy the **appId**, and **password** outputs from the commmand, you will use these in the deployment step

	```
	az ad sp create-for-rbac --name <ServicePrincipalName> --role Contributor
	```

**2. Deploy timeschedule-based aautoscale template**

* Use the applicationId and password properties of your service principal to complete the deployment. 
* You can additionally specify the default timezone that will be used for the autoscale schedule

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fsqldwexternaldata.blob.core.windows.net%2Farmtemplates%2FScheduledAutoScaleDWFleet.json" target="_blank">
<img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazure.png"/>
</a>

**3. Add tags to candidate databases**

> [!NOTE]
>
> A data warehouse must be online to be successfully tagged with values

To use the Logic app on a data warehouse, add the following tags to the data warehouse instance using the format specified below. Note that for the logic app to discover the data warehouse instance, it must belong to the same subscription.

* AutoScheduleScaleUpTime: HH:MM (AM/PM)
* AutoScheduleScaleUpSLO: [\<SQL DW performance level> from SQL DataWarehouse Capacity Settings](https://docs.microsoft.com/en-us/azure/sql-data-warehouse/memory-and-concurrency-limits)
* AutoScheduleScaleDownTime: HH:MM (AM/PM)
* AutoScheduleScaleDownSLO: [\<SQL DW performance level> from SQL DataWarehouse Capacity Settings](https://docs.microsoft.com/en-us/azure/sql-data-warehouse/memory-and-concurrency-limits)

You can optionally specify a timezone that an individual data warehouse will use for the autoscale schedule:

* AutoScheduleTimeZone: [\<TimeZoneName> from Microsoft Time Zone Index Values](https://docs.microsoft.com/en-us/previous-versions/windows/embedded/ms912391(v=winembedded.11))

![tagged data warehouse example](media/scenario3-taggedfleet.png)
