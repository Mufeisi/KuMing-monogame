using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MonoShare.Maui.Controls;

namespace MonoShare.Maui.Pages;

internal sealed class GameHostPage : ContentPage
{
    public GameHostPage()
    {
        BackgroundColor = Colors.Black;
        Padding = new Thickness(0);

        Content = new Grid
        {
            BackgroundColor = Colors.Black,
            Children =
            {
                new MobileGameHostView
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                }
            }
        };
    }
}
