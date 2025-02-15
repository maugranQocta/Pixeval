#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/PersonView.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUI3Utilities.Attributes;
using Pixeval.Controls.Card;

namespace Pixeval.Controls.PersonView;

[DependencyProperty<string>("PersonNickname")]
[DependencyProperty<string>("PersonName")]
[DependencyProperty<Uri>("PersonProfileNavigateUri")]
[DependencyProperty<ImageSource>("PersonPicture")]
public partial class PersonView : UserControl
{
    public PersonView()
    {
        InitializeComponent();
    }

    private async void ContentContainerOnTapped(object sender, TappedRoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(PersonProfileNavigateUri);
    }
}
