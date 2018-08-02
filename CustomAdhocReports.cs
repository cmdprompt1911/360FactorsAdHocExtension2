﻿using Izenda.BI.Framework.CustomConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using Izenda.BI.Framework.Models;
using System.ComponentModel.Composition;
using Izenda.BI.Framework.Models.ReportDesigner;
using Izenda.BI.Framework.Utility;
using Izenda.BI.Framework.Models.Contexts; //For Referencing User Context

namespace CustomAdhocReports
{
    [Export(typeof(IAdHocExtension))]
    class CustomAdhocReports : DefaultAdHocExtension
    {

        /// <summary>
        /// Sets custom filters which are hidden to the user of the interface.
        /// </summary>
        public override ReportFilterSetting SetHiddenFilters(SetHiddenFilterParam param)
        {
            //We want to filter down to a unique value in the CustomerID
            var filterFieldName = "customerCode";

            //Here we can use the UserContext reference to pull the TenantID value from the current users (The TenantID value you chose during the Tenant Setup portion)
            var tenantId = String.Empty;
            if(UserContext.Current.CurrentTenant != null)
                tenantId = UserContext.Current.CurrentTenant.TenantID;

            Func<ReportFilterSetting, int, QuerySource, QuerySourceField, Guid, Relationship, int> addHiddenFilters = (result, filterPosition, querySource, field, equalOperator, rel) =>
            {
                //Here we construct a filter behind the scenes, passing the tenantId variable we made above into the value portion of the filter, so it will only show rows where customerCode = tenantId
                var firstFilter = new ReportFilterField
                {
                    Alias = $"customerCode{filterPosition}",
                    QuerySourceId = querySource.Id,
                    SourceDataObjectName = querySource.Name,
                    QuerySourceType = querySource.Type,
                    QuerySourceFieldId = field.Id,
                    SourceFieldName = field.Name,
                    DataType = field.DataType,
                    Position = filterPosition,
                    OperatorId = equalOperator,
                    Value = tenantId,
                    RelationshipId = rel?.Id,
                    IsParameter = false,
                    ReportFieldAlias = null
                };

                //clean up tenantId for comparison
                var tenantIdAllLower = tenantId.Trim().ToLower();
                //added so if you are logged in at the factor's reseller or ADMIN level, the filter gets left off
                if(tenantIdAllLower != "none" && tenantIdAllLower.Length > 0 && tenantIdAllLower != "rfactors")
                    result.FilterFields.Add(firstFilter);


                var logic = $"({filterPosition})";
                if (string.IsNullOrEmpty(result.Logic))
                {
                    result.Logic = logic;
                }
                else
                {
                    result.Logic += $" AND {logic}";
                }

                return filterPosition;
            };

            var filterSetting = new ReportFilterSetting()
            {
                FilterFields = new List<ReportFilterField>()
            };
            var position = 0;

            var ds = param.ReportDefinition.ReportDataSource;

            // Build the hidden filters for customerCode fields
            foreach (var querySource in param.QuerySources // Scan thru the query sources that are involved in the report
                .Where(x => x.QuerySourceFields.Any(y => filterFieldName.Contains(y.Name))))

            //(filterFieldName, StringComparison.OrdinalIgnoreCase)))) // Take only query sources that have filter field name
            {
                // Pick the relationships that joins the query source as primary source
                // Setting the join ensure the proper table is assigned when using join alias in the UI
                var rels = param.ReportDefinition.ReportRelationship.
                    Where(x => x.JoinQuerySourceId == querySource.Id)
                    .ToList();

                // Count the relationships that the filter query source is foreign query source
                var foreignRelCounts = param.ReportDefinition.ReportRelationship
                    .Where(x => x.ForeignQuerySourceId == querySource.Id)
                    .Count();

                // Find actual filter field in query source
                // var field = querySource.QuerySourceFields.FirstOrDefault(x => x.Name.Equals(filterFieldName, StringComparison.OrdinalIgnoreCase));
                var fields = querySource.QuerySourceFields.Where(x => filterFieldName.Contains(x.Name));

                // Pick the equal operator
                var equalOperator = Izenda.BI.Framework.Enums.FilterOperator.FilterOperator.EqualsManualEntry.GetUid();

                // TAKE THIS OUT LATER
                foreach (var field in fields)
                {
                    // In case there is no relationship that the query source is joined as primary
                    if (rels.Count() == 0)
                    {
                        // Just add hidden filter with null relationship
                        position = addHiddenFilters(filterSetting, position, querySource, field, equalOperator, null);
                    }
                    else
                    {
                        // Add another hidden filter for query source that appears in both alias primary and foreign query source of relationships.
                        // This step is mandatory because when aliasing a primary query source, it becomes another instance of query source in the query. 
                        // So if we only add filter for alias, the original query source instance will not be impacted by the filter. That's why we need
                        // to add another filter for original instance when it appears in both side of alias and foreign.
                        // For example:
                        //          [Order] LEFT JOIN [Employee]
                        //      [Aliased Employee] LEFT JOIN [Department]
                        // If the system needs to add a hidden filter to [Employee], for example: [CompanyId] = 'ALKA'
                        // It needs to add
                        //          [Employee].[CompanyId] = 'ALKA' AND [Aliased Employee].[CompanyId] = 'ALKA'
                        // By this way, it ensures all [Employee] instances are filtered by ALKA company id.
                        if (foreignRelCounts > 0)
                        {
                            position = addHiddenFilters(filterSetting, position, querySource, field, equalOperator, null);
                        }

                        foreach (var rel in rels)
                        {
                            // Loop thru all relationships that the query source is joined as primary and add the hidden field associated with each relationship
                            position = addHiddenFilters(filterSetting, position, querySource, field, equalOperator, rel);
                        }

                    }
                }
            }

            return filterSetting;
        }
    }
}
