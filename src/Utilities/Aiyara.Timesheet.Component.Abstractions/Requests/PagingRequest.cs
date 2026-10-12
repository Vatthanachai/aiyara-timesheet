using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Component.Abstractions.Requests;

/// <summary>
/// Interface for paging request.
/// </summary>
[JsonObject]
public class PagingRequest : IPagingRequest
{
    /// <summary>
    /// Is Order data with desc
    /// </summary>
    [JsonProperty("is_order_descending")]
    [FromQuery(Name = "is_order_descending")]
    [Description("Descending records")]
    public bool IsOrderDescending { get; set; } = false;

    /// <summary>
    /// page index
    /// </summary>
    [JsonProperty("page_index")]
    [FromQuery(Name = "page_index")]
    [Description("Current page index")]
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// page limit
    /// </summary>
    [JsonProperty("page_limit")]
    [FromQuery(Name = "page_limit")]
    [Description("limit record per page")]
    public int PageLimit { get; set; } = 25;
}