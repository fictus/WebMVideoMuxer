namespace WebmEditor;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new MainPage()) { Title = "WebM Audio Editor", Width = 1200, Height = 820 };
}
