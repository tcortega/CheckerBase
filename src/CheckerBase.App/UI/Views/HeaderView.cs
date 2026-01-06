using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI.Views;

/// <summary>
/// Displays ASCII art header banner.
/// </summary>
public sealed class HeaderView : FrameView
{
    private const string AsciiArt = @"
   ██████╗██╗  ██╗███████╗ ██████╗██╗  ██╗███████╗██████╗
  ██╔════╝██║  ██║██╔════╝██╔════╝██║ ██╔╝██╔════╝██╔══██╗
  ██║     ███████║█████╗  ██║     █████╔╝ █████╗  ██████╔╝
  ██║     ██╔══██║██╔══╝  ██║     ██╔═██╗ ██╔══╝  ██╔══██╗
  ╚██████╗██║  ██║███████╗╚██████╗██║  ██╗███████╗██║  ██║
   ╚═════╝╚═╝  ╚═╝╚══════╝ ╚═════╝╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝";

    // Height of ASCII art including padding
    public const int RequiredHeight = 9;

    public HeaderView() : base("")
    {
        Border.BorderStyle = BorderStyle.None;
        Width = Dim.Fill();
        Height = RequiredHeight;

        var label = new Label(AsciiArt)
        {
            X = Pos.Center(),
            Y = 0
        };

        // Use cyan color for the ASCII art
        label.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
        };

        Add(label);
    }
}
