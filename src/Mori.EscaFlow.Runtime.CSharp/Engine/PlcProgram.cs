using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>Base of every generated program class.</summary>
public abstract class PlcProgram
{
    protected PlcProgram(PlcController controller, string name)
    {
        Controller = controller;
        Name = name;
    }

    public PlcController Controller { get; }
    public string Name { get; }
    public LogixServices Services => Controller.Services;
    public SystemStatus Sys => Controller.Sys;

    /// <summary>One scan of the program (the main routine).</summary>
    public abstract void Main();

    /// <summary>Prescan (init hook), run once when the controller enters Run.</summary>
    public virtual void Prescan()
    {
    }

    /// <summary>Fault routine, run when the program throws during a scan.</summary>
    public virtual void Fault()
    {
    }

    public override string ToString() => Name;
}

/// <summary>A program bound to its generated controller type.</summary>
public abstract class PlcProgram<TController> : PlcProgram where TController : PlcController
{
    protected PlcProgram(TController controller, string name) : base(controller, name) => C = controller;

    /// <summary>The typed controller: controller tags, module images and other program instances.</summary>
    public TController C { get; }
}

/// <summary>Base of every generated add-on instruction class; the instance lives in tag memory like a structure.</summary>
public abstract class FunctionBlock : PlcStruct
{
    protected FunctionBlock(Tag tag, int offset, PlcController controller) : base(tag, offset) => Controller = controller;

    public PlcController Controller { get; }
    public LogixServices Services => Controller.Services;
    public SystemStatus Sys => Controller.Sys;

    public virtual void Prescan()
    {
    }

    public virtual void Postscan()
    {
    }

    public virtual void EnableInFalse()
    {
    }
}
