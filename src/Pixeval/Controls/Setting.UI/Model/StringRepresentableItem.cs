#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/IStringRepresentableItem.cs
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
using System.Collections.Generic;
using Pixeval.Attributes;

namespace Pixeval.Controls.Setting.UI.Model;

public record StringRepresentableItem(Enum Item, string StringRepresentation)
{
    public StringRepresentableItem(Enum item) : this(item, item.GetLocalizedResourceContent()!)
    {
    }

    public override string ToString() => StringRepresentation;
}

public interface IAvailableItems
{
    // ReSharper disable once UnusedMember.Global
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    static IEnumerable<StringRepresentableItem>? AvailableItems { get; }
}
