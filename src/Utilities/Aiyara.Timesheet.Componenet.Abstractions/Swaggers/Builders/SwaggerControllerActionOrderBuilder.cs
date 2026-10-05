using Aiyara.Timesheet.Component.Abstractions.Swaggers.Attributes;
using Aiyara.Timesheet.Component.Abstractions.Swaggers.Helpers;

using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Aiyara.Timesheet.Component.Abstractions.Swaggers.Builders;

public class SwaggerControllerActionOrderBuilder(ActionDescriptor actionDescriptor)
{
    protected const string ControllerKey = "controller";
    protected const string ActionKey = "action";

    protected ActionDescriptor ActionDescriptor { get; } = actionDescriptor;

    public string Build()
    {
        var controllerOrder = GetControllerOrder();
        var actionOrder = GetActionOrder();
        string controllerName = GetControllerName();
        string actionName = GetActionName();

        bool isSwaggerActionOrderAttribute = SwaggerAttributeHelper.IsTypeOfSwaggerActionOrderAttribute(actionOrder.AttributeType);
        string suffixOfActionOrder = isSwaggerActionOrderAttribute ? $"_{GenerateOrderKey(actionOrder)}_{actionName}" : string.Empty;

        return $"{GenerateOrderKey(controllerOrder)}_{controllerName}{suffixOfActionOrder}";
    }

    protected ISwaggerAttributeOrder GetControllerOrder()
    {
        var attribute = ActionDescriptor.EndpointMetadata.FirstOrDefault(f => f is SwaggerControllerOrderAttribute);
        return (attribute as ISwaggerAttributeOrder) ?? new DefaultSwaggerOrder();
    }

    protected ISwaggerAttributeOrder GetActionOrder()
    {
        var attribute = ActionDescriptor.EndpointMetadata.FirstOrDefault(f => f is SwaggerActionOrderAttribute);
        return (attribute as ISwaggerAttributeOrder) ?? new DefaultSwaggerOrder();
    }

    protected string GetControllerName()
        => $"{ActionDescriptor.RouteValues[ControllerKey]}";

    protected string GetActionName()
        => $"{ActionDescriptor.RouteValues[ActionKey]}";

    protected string GenerateOrderKey(ISwaggerAttributeOrder swaggerAttributeOrder)
        => swaggerAttributeOrder.Order.ToString("D10");
}