using System;
using AntivirusApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AntivirusApp.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage()
    {
        InitializeComponent();

        EnterButton.Click += (_, _) => Submit();
        PassBox.KeyDown += OnKeyDown;
        UserBox.KeyDown += OnKeyDown;

        Loaded += (_, _) =>
        {
            Helpers.Anim.FadeIn(PageRoot);
            Helpers.Anim.Pop(LogoBox);
            Helpers.Anim.SlideIn(EnterButton, 80, 10);
            UserBox.Focus(FocusState.Programmatic);
        };
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) Submit();
    }

    private void Submit()
    {
        string user = (UserBox.Text ?? string.Empty).Trim();
        string password = PassBox.Password ?? string.Empty;

        if (!AuthService.Validate(user, password))
        {
            ErrorText.Text = "Usuario o contraseña incorrectos.";
            ErrorText.Visibility = Visibility.Visible;
            Helpers.Anim.Pulse(ErrorText, 1.03, 200);
            PassBox.Password = string.Empty;
            PassBox.Focus(FocusState.Programmatic);
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        AuthService.CurrentUser = user;
        App.Log("Login correcto: " + user);

        Frame.Navigate(typeof(ShellPage));
    }
}
