namespace Aiyara.Timesheet.Component.Abstractions.Helpers.Assemblies;

public class DefaultAssemblyHelper<T>() : AssemblyHelper(typeof(T))
    where T : AssemblyHelper, new()
{
    public static T Instance => new();
}