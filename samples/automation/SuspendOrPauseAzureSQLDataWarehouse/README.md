# Suspend or Pause Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse)

See the blog post [here](https://blogs.msdn.microsoft.com/allanmiller/2017/09/20/pausing-azure-sql-data-warehouse-using-an-automation-runbook/ "Pausing Azure SQL Data Warehouse using an Automation Runbook")

### General Information
This is a simple PowerShell Workflow that is designed to be ran from an Azure Automation account to pause your Azure SQL Data Warehouse using Suspend-AzureRmSQLDatabase with checks to see if there are queries running before pausing.

More to come!
