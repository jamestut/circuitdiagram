﻿// This file is part of Circuit Diagram.
// http://www.circuit-diagram.org/
// 
// Copyright (c) 2019 Samuel Fisher
//  
// Circuit Diagram is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CircuitDiagram.Circuit.Metadata;
using CircuitDiagram.Primitives;

namespace CircuitDiagram.Circuit
{
    public class CircuitDocument : IReadOnlyCircuitDocument
    {
        public CircuitDocument()
        {
            Elements = new List<IElement>();
            Metadata = new CircuitDocumentMetadata();
        }

        public CircuitDocumentMetadata Metadata { get; }

        public Size Size { get; set; }

        public ICollection<IElement> Elements { get; }

        IEnumerable<IElement> IReadOnlyCircuitDocument.Elements => Elements;
    }
}
