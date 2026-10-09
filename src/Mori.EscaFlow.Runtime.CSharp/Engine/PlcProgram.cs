using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Base of every generated program class.
/// </summary>
/// <remarks>
/// A Logix program is a scope (its own tags) plus routines; one routine is the main routine the task calls each
/// scan, another may be the fault routine. The generated class holds the program tags as wrapper fields, each
/// routine as a method, <see cref="Main"/> calling the main routine, and <see cref="Prescan"/> holding the
/// prescan behaviour the importer synthesised from the routines.
/// </remarks>
public abstract class PlcProgram
{
    /// <summary>Binds the program to its controller.</summary>
    protected PlcProgram(PlcController controller, string name)
    {
        Controller = controller;
        Name = name;
    }

    /// <summary>The owning controller.</summary>
    public PlcController Controller { get; }

    /// <summary>Program name as in the project.</summary>
    public string Name { get; }

    /// <summary>The controller's vendor services (GSV, SSV, MSG, ...).</summary>
    public LogixServices Services => Controller.Services;

    /// <summary>The controller's system status (S:FS and friends).</summary>
    public SystemStatus Sys => Controller.Sys;

    /// <summary>One scan of the program (the main routine).</summary>
    public abstract void Main();

    /// <summary>Prescan (init hook), run once when the controller enters Run.</summary>
    /// <remarks>Logix prescans every routine of every program before the first scan: OTE outputs are cleared, one-shot storage bits set, timers and counters put in their prescan state, add-on instances prescanned. The generated override reproduces exactly that for this program.</remarks>
    public virtual void Prescan()
    {
    }

    /// <summary>Fault routine, run when the program throws during a scan.</summary>
    public virtual void Fault()
    {
    }

    /// <summary>The program name, for diagnostics.</summary>
    public override string ToString() => Name;
}

/// <summary>A program bound to its generated controller type.</summary>
/// <typeparam name="TController">The generated controller class, so program code reaches controller tags with static typing.</typeparam>
public abstract class PlcProgram<TController> : PlcProgram where TController : PlcController
{
    /// <summary>Binds the program to its typed controller.</summary>
    protected PlcProgram(TController controller, string name) : base(controller, name) => C = controller;

    /// <summary>The typed controller: controller tags, module images and other program instances.</summary>
    public TController C { get; }
}

/// <summary>
/// Base of every generated add-on instruction class; the instance lives in tag memory like a structure.
/// </summary>
/// <remarks>
/// An add-on instruction instance is a tag of the add-on's backing structure (EnableIn, EnableOut, the parameters
/// and the local tags), so the class derives from <see cref="PlcStruct"/> and its fields are views into that tag.
/// The generated <c>Execute(bool enableIn)</c> reproduces the Logix call protocol: EnableIn is written, a false
/// EnableIn runs <see cref="EnableInFalse"/> instead of the logic (when the definition asks for it), otherwise the
/// Logic routine runs with EnableOut true. <see cref="Prescan"/> and <see cref="Postscan"/> are the scan-mode
/// routines the controller invokes on entering Run and when an SFC step deactivates.
/// </remarks>
public abstract class FunctionBlock : PlcStruct
{
    /// <summary>Binds the instance to its backing tag memory and controller.</summary>
    protected FunctionBlock(Tag tag, int offset, PlcController controller) : base(tag, offset) => Controller = controller;

    /// <summary>The owning controller.</summary>
    public PlcController Controller { get; }

    /// <summary>The controller's vendor services.</summary>
    public LogixServices Services => Controller.Services;

    /// <summary>The controller's system status.</summary>
    public SystemStatus Sys => Controller.Sys;

    /// <summary>Prescan: the synthesized prescan of the Logic routine followed by the definition's Prescan routine, when it executes one.</summary>
    public virtual void Prescan()
    {
    }

    /// <summary>Postscan: the definition's Postscan routine, run when an enclosing SFC action deactivates.</summary>
    public virtual void Postscan()
    {
    }

    /// <summary>Called instead of the logic when EnableIn is false and the definition executes an EnableInFalse routine.</summary>
    public virtual void EnableInFalse()
    {
    }
}
