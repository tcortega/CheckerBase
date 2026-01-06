using CheckerBase.App.UI;
using Terminal.Gui;

// Initialize Terminal.Gui
Application.Init();

try
{
    // Create and run the main window
    using var mainWindow = new MainWindow();
    Application.Run(mainWindow);
}
catch (Exception ex)
{
    // Shutdown Terminal.Gui first to restore console
    Application.Shutdown();

    // Then show the error
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine(ex.StackTrace);

    Environment.Exit(1);
}
finally
{
    // Ensure Terminal.Gui is properly shut down
    Application.Shutdown();
}
