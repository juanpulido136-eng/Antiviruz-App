using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntivirusApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AntivirusApp.Views;

public sealed partial class AccountPage : Page
{
    private readonly UserProfile _profile = ProfileStore.Load();
    private bool _avatarAnimated;

    public AccountPage()
    {
        InitializeComponent();

        PickPhotoButton.Click += async (_, _) => await PickPhotoAsync();
        ClearPhotoButton.Click += (_, _) => ClearPhoto();
        SaveButton.Click += (_, _) => Save();

        Fill();
        Loaded += (_, _) => Helpers.Anim.FadeIn(PageRoot);
    }

    private void Fill()
    {
        NameBox.Text = _profile.DisplayName;
        DescBox.Text = _profile.Description;

        ApplyAvatar();

        HeaderName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? "Cuenta"
            : _profile.DisplayName;

        HeaderDesc.Text = string.IsNullOrWhiteSpace(_profile.Description)
            ? "Sin descripción todavía. Añádela abajo."
            : _profile.Description;

        SessionUser.Text = AuthService.CurrentUser;
        SessionProfile.Text = Path.Combine(ProfileStore.Folder, "profile.json");
        SessionPhoto.Text = string.IsNullOrWhiteSpace(_profile.AvatarPath)
            ? "Sin foto (se muestran tus iniciales)"
            : _profile.AvatarPath;
    }

    private void ApplyAvatar()
    {
        Avatar.DisplayName = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? AuthService.CurrentUser
            : _profile.DisplayName;

        if (!string.IsNullOrWhiteSpace(_profile.AvatarPath) && File.Exists(_profile.AvatarPath))
        {
            Avatar.ProfilePicture = new BitmapImage(new Uri(_profile.AvatarPath));
            Avatar.Initials = string.Empty;
        }
        else
        {
            Avatar.ProfilePicture = null;
            Avatar.Initials = ProfileStore.Initials(_profile.DisplayName);
        }

        if (!_avatarAnimated)
        {
            _avatarAnimated = true;
            Helpers.Anim.Pop(Avatar);
        }
    }

    private async System.Threading.Tasks.Task PickPhotoAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
                picker.FileTypeFilter.Add(extension);

            var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            _profile.AvatarPath = ProfileStore.SetAvatar(file.Path);
            ApplyAvatar();

            SessionPhoto.Text = _profile.AvatarPath;
            StatusText.Text = "Foto actualizada. Pulsa Guardar cambios para fijarla.";
        }
        catch (Exception ex)
        {
            App.Log("AccountPage.PickPhoto FALLÓ", ex);
            StatusText.Text = "No se pudo cargar la foto: " + ex.Message;
        }
    }

    private void ClearPhoto()
    {
        try
        {
            ProfileStore.ClearAvatar();
            _profile.AvatarPath = string.Empty;
            ApplyAvatar();

            SessionPhoto.Text = "Sin foto (se muestran tus iniciales)";
            StatusText.Text = "Foto quitada. Pulsa Guardar cambios para fijarlo.";
        }
        catch (Exception ex)
        {
            App.Log("AccountPage.ClearPhoto FALLÓ", ex);
            StatusText.Text = "No se pudo quitar la foto: " + ex.Message;
        }
    }

    private void Save()
    {
        _profile.DisplayName = (NameBox.Text ?? string.Empty).Trim();
        _profile.Description = (DescBox.Text ?? string.Empty).Trim();

        ProfileStore.Save(_profile);

        HeaderName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName) ? "Cuenta" : _profile.DisplayName;
        HeaderDesc.Text = string.IsNullOrWhiteSpace(_profile.Description)
            ? "Sin descripción todavía. Añádela abajo."
            : _profile.Description;

        ApplyAvatar();

        StatusText.Text = "Guardado · " + DateTime.Now.ToString("HH:mm:ss");

        NotificationService.Summary("\uE73E", "Cuenta", "Perfil guardado",
            string.IsNullOrWhiteSpace(_profile.DisplayName) ? "Datos de cuenta actualizados." : _profile.DisplayName,
            "OkBrush");
    }
}
