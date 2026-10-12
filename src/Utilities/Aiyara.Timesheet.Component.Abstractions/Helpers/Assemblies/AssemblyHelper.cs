using System.Diagnostics;
using System.Reflection;

namespace Aiyara.Timesheet.Component.Abstractions.Helpers.Assemblies;

public abstract class AssemblyHelper(Type type)
{
    protected Type Type { get; } = type;

    public virtual Assembly Assembly => Type.Assembly;

    public virtual string AssemblyName => $"{Assembly.GetName().Name}";
    public virtual string Version => $"{Assembly.GetName().Version}";

    public virtual string FileVersion
    {
        get
        {
            var info = FileVersionInfo.GetVersionInfo(Assembly.Location);
            return $"{info.FileVersion}";
        }
    }
}