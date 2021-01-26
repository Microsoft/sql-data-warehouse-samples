## Introduction

The following scripts are leveraged to create custom monitoring dashboard widgets in SQL Operations Studio (preview) for Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse). 

## Current Widgets


* **Table Health Count** - Lists the number of tables which may be suffering form low quality segments and number of tables with statistics which have not been updated in the last 7 days
* **User Activities** - Lists the number of active sessions, active queries, and queued queries
* **Storage Size** - Shows the total database size excluding the unallocated space
* **Data Distribution** - Shows the storage distribution across all distribution databases to help detect skew
* **Memory consumption percentage** - Shows the SQL Server memory consumption across all compute nodes
* **Tempdb space utilization** - Shows the tempdb memory consumption across all compute nodes

## Instructions

Copy this directory to your local machine. To update your SQL Operatios Studio (sqlops) dashboard, open sqlops and press Ctrl+Comma. Ensure the User Settings.json is synced with the current copy in this repo. Then make sure the 'queryFile' path backing each dashboard widget in User Settings.json is referring to the corresponding monitoring script in your local repo.

## Future Enhancements
* Specify Compute vs Control node
* Add details view for each widget
* QDS integration with SQLDW
* Azure portal metrics into SQL Ops

## Images

![alt text](https://github.com/Microsoft/sql-data-warehouse-samples/blob/master/samples/sqlops/MonitoringScripts/images/insight_widget_0.PNG)

![alt text](https://github.com/Microsoft/sql-data-warehouse-samples/blob/master/samples/sqlops/MonitoringScripts/images/insight_widget_1.PNG)


 






