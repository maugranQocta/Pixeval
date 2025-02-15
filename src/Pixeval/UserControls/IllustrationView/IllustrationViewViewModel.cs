#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/IllustrationGridViewModel.cs
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

using System.Linq;
using CommunityToolkit.WinUI.UI;
using Pixeval.Options;
using Pixeval.Util;
using Pixeval.Utilities;

namespace Pixeval.UserControls.IllustrationView;

public sealed class IllustrationViewViewModel : SortableIllustrationViewViewModel
{
    #region RiverFlowLayout

    private ThumbnailDirection _thumbnailDirection;

    private static double _itemWidth;

    public static double StaticItemHeight;

    public ThumbnailDirection ThumbnailDirection
    {
        get => _thumbnailDirection;
        set
        {
            if (_thumbnailDirection == value) 
                return;
            _thumbnailDirection = value;
            switch (_thumbnailDirection)
            {
                case ThumbnailDirection.Landscape:
                    ItemHeight = 180;
                    ItemWidth = 250;
                    break;
                case ThumbnailDirection.Portrait:
                    ItemHeight = 250;
                    ItemWidth = 180;
                    break;
                default:
                    WinUI3Utilities.ThrowHelper.ArgumentOutOfRange(_thumbnailDirection);
                    break;
            }
        }
    }

    public double ItemWidth
    {
        get => _itemWidth;
        set => _itemWidth = value;
    }

    public double ItemHeight
    {
        get => StaticItemHeight;
        set
        {
            // 需要通过绑定更新
            if (StaticItemHeight == value)
                return;
            OnPropertyChanging();
            StaticItemHeight = value;
            OnPropertyChanged();
        }
    }

    #endregion

    public override IIllustrationViewDataProvider DataProvider { get; }

    public IllustrationViewViewModel()
    {
        SelectionLabel = IllustrationViewCommandBarResources.CancelSelectionButtonDefaultLabel;
        DataProvider = new IllustrationViewDataProvider();
        DataProvider.SelectedIllustrations.CollectionChanged += (_, _) =>
        {
            IsAnyIllustrationSelected = DataProvider.SelectedIllustrations.Count > 0;
            var count = DataProvider.SelectedIllustrations.Count;
            SelectionLabel = count == 0
                ? IllustrationViewCommandBarResources.CancelSelectionButtonDefaultLabel
                : IllustrationViewCommandBarResources.CancelSelectionButtonFormatted.Format(count);
        };
    }

    public override void Dispose()
    {
        DataProvider.FetchEngine?.Cancel();
        DataProvider.DisposeCurrent();
    }

    public override void SetSortDescription(SortDescription description)
    {
        if (!DataProvider.IllustrationsView.SortDescriptions.Any())
        {
            DataProvider.IllustrationsView.SortDescriptions.Add(description);
            return;
        }

        DataProvider.IllustrationsView.SortDescriptions[0] = description;
    }

    public override void ClearSortDescription()
    {
        DataProvider.IllustrationsView.SortDescriptions.Clear();
    }
}
