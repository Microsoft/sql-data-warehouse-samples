# Database queries

This folder contains a collection of scripts used to monitor query usage in your [Azure Synapse Analytics SQL Pool (formerly SQL Data Warehouse)](http://aka.ms/sqldw) database. 

 - [query_memory_usage](query_memory_usage.sql): This query shows the memory utilization of queries run against your data warehouse. It allows you to easily identify any query that could benefit from additional memory via a larger resource class.