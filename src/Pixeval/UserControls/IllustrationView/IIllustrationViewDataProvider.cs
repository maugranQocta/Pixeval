﻿#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/IllustrationViewDataProvider.cs
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
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI;
using Pixeval.CoreApi.Engine;
using Pixeval.CoreApi.Model;

namespace Pixeval.UserControls.IllustrationView;

public interface IIllustrationViewDataProvider
{
    /// <summary>
    /// Avoid using <see cref="AdvancedCollectionView.Filter"/>, if you want to set the filter, use <see cref="Filter"/>
    /// </summary>
    AdvancedCollectionView IllustrationsView { get; }
    
    ObservableCollection<IllustrationViewModel> IllustrationsSource { get; }
    
    IFetchEngine<Illustration?>? FetchEngine { get; }

    Predicate<object>? Filter { get; set; }

    event EventHandler FilterChanged;

    void DisposeCurrent();

    ObservableCollection<IllustrationViewModel> SelectedIllustrations { get; }

    Task<int> LoadMore();

    Task<int> FillAsync(int? itemsLimit = null);

    Task<int> ResetAndFillAsync(IFetchEngine<Illustration?>? fetchEngine, int? itemLimit = null);
}