# Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse) AutoScale using Logic apps
 Hub Spoke Template with SQL Databases

<a href="https://ms.portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Fsql-data-warehouse-samples%2Fmaster%2Farm-templates%2FsqlDwSpokeDbTemplate%2Fazuredeploy.json" target="_blank">
<img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazure.png"/>
</a>

This template will deploy a Premium SQL Database Elastic Pool, a specified number of Premium Databases in the Elastic Pool, an Automation Account, and a Runbook.

The Automation Account is used to kick off a Runbook that will run a series of scripts on behalf of the user to set up the necessary stored procedures, tables, and views necessary to manage the various states of the SQL Database spoke external tables to the corresponding tables in the SQL Data Warehouse.

The Runbook will deploy the following tables and stored procedures within the SQL Data Warehouse instance:

- [meta].[DatamartControlTable]

  - This table defines which objects each database has permissions to view. Any additions or changes to this table need to be propagated through the solution by running [meta].[GenerateDatamartExternalTableDefinitionsAndGrantSelect]. 

- [meta].[DatamartExternalTableDefinitions]

  - This table stores the DDL statements corresponding to the different tables that each of the SQL Database spokes have permissions. This table is useful for auditing purposes to examine access policy. 

- [meta].[AddObjectsForDatamartUserToControlTable]

  - This stored procedure makes simpler the task of adding tables to the DatamartControlTable. This procedure automates adding specific objects or adding an entire schema to the control table for a specific user.

- [meta].[CreateExternalTableFromDw]

  - This stored procedure generates the DDL statements which the SQL Database spoke instances will use to generate external table definitions.

- [meta].[GenerateDatamartExternalTableDefinitionsAndGrantSelect]

  - This stored procedure checks the DatamartControlTable to determine what DDL statements to generate and what SELECT permissions to grant. This stored procedure assumes only GRANT and will not automatically revoke SELECT permissions when the DatamartControlTable is changed. 

    ​