# Azure Automation Samples

This is a collection of samples that demonstrate how to use [Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse) AutoScale using Logic apps
](https://aka.ms/sqldw) with [Azure Automation](https://azure.microsoft.com/services/automation).

When using the SQL Azure Data Warehouse service (ADW) it can be important to automate operations. One of these goals of this project is to help with the pause, resume and scale of the data warehouse.

## Pause, Resume, Scale Workflows

If you have an automated ETL/ELT process, it can be important to properly manage the compute of the ADW. The Pause and Resume are a very handy at this level becaue they cannot be managed as a SQL operation. It is possible to scale the data warehouse using the [ALTER DATABASE](https://docs.microsoft.com/en-us/sql/t-sql/statements/alter-database-azure-sql-data-warehouse) statement when the service is in a running state. One of the reasons we created these workflows was to help you find the right time to pause or scale.

## When to Pause or Scale ADW

Your Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse) AutoScale using Logic apps
 is not a regular SQL server. Though it has many similarities, many of request of the service cause [data movement operations](https://docs.microsoft.com/en-us/azure/sql-data-warehouse/sql-data-warehouse-tables-distribute#understanding-data-movement) which can be time consuming. When you pause/scale the service, all running queries are canceled. Other than having lost productivity, rollback of queries that are in the middle of data movement could take hours to complete. While the rollback operations are processing the compute will be running but you will not be able to connect or submit request. These workflows will test to ensure that is is OK to pause or scale before taking action. Though there are retry sequences in the workflows, it will also be important to ensure that the data warehouse is in the right state before your processes continue.

## How to Use Workflows

Though you can download these workflows an run them from PowerShell, even call them from an SSIS task, they were designed to be ran from an Azure Automation Account.

After importing the runbook(s), you will need to manually create a credential for connectivity to the data warehouse. The credential you create will be for a SQL account that has access to read from system DMVs. When calling or scheduling the runbook, the credential will be passed in as a parameter.

Each workflow soon will have its own documentation.