﻿#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/QrCodePresenter.xaml.cs
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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pixeval.Popups;

public sealed partial class QrCodePresenter : IAppPopupContent
{
    private readonly ImageSource _qrCodeImageSource;

    public QrCodePresenter(ImageSource qrCodeImageSource)
    {
        _qrCodeImageSource = qrCodeImageSource;
        UniqueId = Guid.NewGuid();
        InitializeComponent();
    }

    public Guid UniqueId { get; }

    public FrameworkElement UIContent => this;

    private void QrCodeImage_OnLoaded(object sender, RoutedEventArgs e)
    {
        QrCodeImage.Source = _qrCodeImageSource;
    }
}