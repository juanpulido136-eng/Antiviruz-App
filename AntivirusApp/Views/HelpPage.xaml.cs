using AntivirusApp.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AntivirusApp.Views;

public sealed partial class HelpPage : Page
{
    public HelpPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Anim.FadeIn(PageRoot);
            Anim.FadeIn(HeroCard, 40);
        };
    }
}
