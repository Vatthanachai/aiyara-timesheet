namespace Aiyara.Timesheet.Component.Abstractions.Swaggers.Attributes;

public class DefaultSwaggerOrder : ISwaggerAttributeOrder
{
    public uint Order => uint.MinValue;
    public Type AttributeType => typeof(DefaultSwaggerOrder);
}