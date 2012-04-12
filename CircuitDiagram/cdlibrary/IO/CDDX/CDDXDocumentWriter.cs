﻿// CDDXDocumentWriter.cs
//
// Circuit Diagram http://www.circuit-diagram.org/
//
// Copyright (C) 2012  Sam Fisher
//
// This program is free software; you can redistribute it and/or
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
using System.IO;
using System.IO.Packaging;
using System.Xml;
using CircuitDiagram.Components;

namespace CircuitDiagram.IO.CDDX
{
    static class CDDXDocumentWriter
    {
        public const double CDDXDocumentVersion = 1.0;
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static void WriteCDDX(CircuitDocument document, Package package, PackagePart documentPart, CDDXSaveOptions saveOptions)
        {
            using (var stream = documentPart.GetStream(FileMode.Create))
            {
                XmlTextWriter writer = new XmlTextWriter(stream, Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                writer.WriteStartDocument();
                writer.WriteStartElement("circuit", "http://schemas.circuit-diagram.org/circuitDiagramDocument/2012/document");
                writer.WriteAttributeString("version", String.Format("{0:0.0}", CDDXDocumentVersion));
                writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);

                // Document size
                writer.WriteStartElement("properties");
                writer.WriteElementString("width", document.Size.Width.ToString());
                writer.WriteElementString("height", document.Size.Height.ToString());
                writer.WriteEndElement();

                // Component sources
                List<ComponentDescription> descriptionsInDocument = new List<ComponentDescription>();
                foreach (Component component in document.Components)
                    if (!descriptionsInDocument.Contains(component.Description))
                        descriptionsInDocument.Add(component.Description);

                int internalIdCounter = 0;
                Dictionary<ComponentSourceLocation, List<ComponentSource>> componentSourcesByLocation = new Dictionary<ComponentSourceLocation, List<ComponentSource>>();
                Dictionary<ComponentDescription, ComponentSource> processedDescriptions = new Dictionary<ComponentDescription, ComponentSource>();
                foreach (ComponentDescription description in descriptionsInDocument)
                {
                    // skip if wire
                    if (description == ComponentHelper.WireDescription)
                        continue;

                    //if (processedDescriptions.ContainsKey(description))
                    //    continue;

                    string relationshipID = null;

                    #region Embed description file
                    // Check if should embed
                    if (saveOptions.EmbedComponents == CDDXSaveOptions.ComponentsToEmbed.All ||
                        (saveOptions.EmbedComponents == CDDXSaveOptions.ComponentsToEmbed.Automatic && ComponentHelper.ShouldEmbedDescription(description)) ||
                        (saveOptions.EmbedComponents == CDDXSaveOptions.ComponentsToEmbed.Custom && saveOptions.CustomEmbedComponents.Contains(description)))
                    {
                        // Check if already embedded
                        bool needToEmbed = true;
                        foreach (ComponentSourceLocation location in componentSourcesByLocation.Keys)
                            if (location.OriginalSource == description.Source)
                            {
                                needToEmbed = false;
                                relationshipID = location.RelationshipID;
                                break;
                            }

                        if (!needToEmbed)
                            continue;

                        // Check whether original file should be embedded
                        bool allDescriptionsAreUsed = true;
                        foreach (ComponentDescription testDescription in description.Source.ContainedDescriptions)
                            if (!descriptionsInDocument.Contains(testDescription))
                            {
                                allDescriptionsAreUsed = false;
                                break;
                            }

                        if (allDescriptionsAreUsed)
                        {
                            // Embed original file
                            string contentType = System.Net.Mime.MediaTypeNames.Text.Xml;
                            if (description.Metadata.Type.ToLower().Contains("cdcom"))
                                contentType = ContentTypes.BinaryComponent;

                            Uri descriptionPath = new Uri("circuitdiagram/components/" + Path.GetFileName(description.Source.Path).Replace(' ', '_'), UriKind.Relative);
                            PackagePart descriptionPart = package.CreatePart(PackUriHelper.CreatePartUri(descriptionPath), contentType, CompressionOption.Normal);
                            using (Stream descriptionStream = descriptionPart.GetStream(FileMode.Create))
                            {
                                byte[] buffer = File.ReadAllBytes(description.Source.Path);
                                descriptionStream.Write(buffer, 0, buffer.Length);
                            }
                            PackageRelationship relationship = documentPart.CreateRelationship(descriptionPart.Uri, TargetMode.Internal, CDDX.RelationshipTypes.IncludedComponent);

                            relationshipID = relationship.Id;
                        }
                        else
                        {
                            // Embed only used description - serialize as binary
                            Uri descriptionPath = new Uri("circuitdiagram/components/" + description.ComponentName.Replace(' ', '_') + ".cdcom", UriKind.Relative);
                            int addedInt = 0;
                            while (package.PartExists(descriptionPath))
                            {
                                descriptionPath = new Uri("circuitdiagram/components/" + description.ComponentName.Replace(' ', '_') + addedInt.ToString() + ".cdcom", UriKind.Relative);
                                addedInt++;
                            }

                            PackagePart descriptionPart = package.CreatePart(PackUriHelper.CreatePartUri(descriptionPath), ContentTypes.BinaryComponent, CompressionOption.Normal);
                            using (var descriptionStream = descriptionPart.GetStream(FileMode.Create))
                            {
                                BinaryWriter descriptionWriter = new BinaryWriter(descriptionStream, new BinaryWriter.BinaryWriterSettings());
                                descriptionWriter.Descriptions.Add(description);
                                descriptionWriter.Write();
                            }
                            PackageRelationship relationship = documentPart.CreateRelationship(descriptionPart.Uri, TargetMode.Internal, CDDX.RelationshipTypes.IncludedComponent);

                            relationshipID = relationship.Id;
                        }
                    }
                    else
                    {
                        // Check to see if embedded anyway & set relationshipID
                        foreach (KeyValuePair<ComponentSourceLocation, List<ComponentSource>> pair in componentSourcesByLocation)
                        {
                            if (pair.Key.OriginalSource == description.Source)
                            {
                                relationshipID = pair.Key.RelationshipID;
                                break;
                            }
                        }
                    }
                    #endregion

                    // Try to group with other descriptions with same RelationshipID and DefinitionSource
                    bool foundGroupMatch = false;
                    foreach (ComponentSourceLocation location in componentSourcesByLocation.Keys)
                    {
                        if (location.RelationshipID == relationshipID && location.DefinitionSource == description.Metadata.ImplementSet)
                        {
                            // Can group
                            foundGroupMatch = true;

                            ComponentSource newSource = new ComponentSource(internalIdCounter.ToString(), (relationshipID != null ? description.ID : null), description.ComponentName, description.Metadata.GUID);

                            // Implementations
                            newSource.ImplementationName = description.Metadata.ImplementItem;
                            foreach (ComponentConfiguration configuration in description.Metadata.Configurations)
                                if (!String.IsNullOrEmpty(configuration.ImplementationName) && ConfigurationIsUsed(description, configuration, document))
                                    newSource.ConfigurationImplementations.Add(new ComponentSource.ConfigurationImplementation(configuration.ImplementationName, configuration.Name));

                            componentSourcesByLocation[location].Add(newSource);
                            processedDescriptions.Add(description, newSource);
                            internalIdCounter++;
                            break;
                        }
                    }

                    if (!foundGroupMatch)
                    {
                        // Create a new source location
                        ComponentSourceLocation newSourceLocation = new ComponentSourceLocation(description.Source, relationshipID, description.Metadata.ImplementSet);
                        componentSourcesByLocation.Add(newSourceLocation, new List<ComponentSource>(1));
                        ComponentSource newSource = new ComponentSource(internalIdCounter.ToString(), (relationshipID != null ? description.ID : null), description.ComponentName, description.Metadata.GUID);

                        // Implementations
                        newSource.ImplementationName = description.Metadata.ImplementItem;
                        foreach (ComponentConfiguration configuration in description.Metadata.Configurations)
                            if (!String.IsNullOrEmpty(configuration.ImplementationName) && ConfigurationIsUsed(description, configuration, document))
                                newSource.ConfigurationImplementations.Add(new ComponentSource.ConfigurationImplementation(configuration.ImplementationName, configuration.Name));

                        componentSourcesByLocation[newSourceLocation].Add(newSource);
                        processedDescriptions.Add(description, newSource);
                        internalIdCounter++;
                    }
                }

                // Generate component IDs
                Dictionary<Component, string> componentIDs = new Dictionary<Component, string>(document.Components.Count());
                int componentIdCounter = 0;
                foreach (Component component in document.Components)
                    if (component.Description.ComponentName.ToLower() != "wire")
                    {
                        componentIDs.Add(component, componentIdCounter.ToString());
                        componentIdCounter++;
                    }

                // Write component sources
                WriteComponentSources(writer, componentSourcesByLocation);

                // Write elements
                WriteDocumentElements(writer, document, processedDescriptions, componentIDs, saveOptions.IncludeLayout, saveOptions.IncludeConnections);

                writer.WriteEndElement();
                writer.WriteEndDocument();
                writer.Flush();
            }
        }

        static bool ConfigurationIsUsed(ComponentDescription description, ComponentConfiguration configuration, CircuitDocument document)
        {
            foreach (Component component in document.Components)
            {
                if (component.Description == description && configuration.Matches(component))
                    return true;
            }

            return false;
        }

        static void WriteComponentSources(XmlTextWriter writer, Dictionary<ComponentSourceLocation, List<ComponentSource>> sourcesByLocation)
        {
            writer.WriteStartElement("definitions");
            foreach (KeyValuePair<ComponentSourceLocation, List<ComponentSource>> source in sourcesByLocation)
            {
                writer.WriteStartElement("src");
                source.Key.Write(writer);

                foreach (ComponentSource source2 in source.Value)
                {
                    writer.WriteStartElement("add");
                    writer.WriteAttributeString("id", source2.InternalID);
                    if (!String.IsNullOrEmpty(source2.ExternalID))
                        writer.WriteAttributeString("xid", source2.ExternalID);
                    writer.WriteAttributeString("name", source2.ComponentName);
                    if (source2.GUID != Guid.Empty)
                        writer.WriteAttributeString("guid", source2.GUID.ToString());
                    if (!String.IsNullOrEmpty(source2.ImplementationName))
                        writer.WriteAttributeString("impl", source2.ImplementationName);
                    foreach (ComponentSource.ConfigurationImplementation implementation in source2.ConfigurationImplementations)
                        implementation.WriteAsNode(writer);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        class ConnectionReference
        {
            public string ID { get; private set; }
            public string Point { get; private set; }

            public ConnectionReference(string id, string point)
            {
                ID = id;
                Point = point;
            }
        }

        class UniqueConnectionDescription
        {
            public Component Component;
            public ConnectionDescription ConnectionDescription;

            public UniqueConnectionDescription(Component component, ConnectionDescription connectionDescription)
            {
                Component = component;
                ConnectionDescription = connectionDescription;
            }
        }

        static void JoinConnectionCentres(ConnectionCentre destination, ConnectionCentre b)
        {
            while (b.Connected.Count > 0)
            {
                b.Connected[0].ConnectTo(destination.Connected.First());
            }
        }

        static void WriteDocumentElements(XmlTextWriter writer, CircuitDocument document, Dictionary<ComponentDescription, ComponentSource> descriptionRefs, Dictionary<Component, string> componentIDs, bool includeLayout, bool includeConnections)
        {
            #region Build connections
            // Remove wires from connections
            foreach (Component component in document.Components)
            {
                if (component.Description.ComponentName.ToLower() == "wire")
                {
                    ConnectionCentre destination = null;

                    foreach (var connection in component.GetConnectedConnections())
                    {
                        if (destination == null)
                        {
                            destination = connection.Centre;
                            connection.Centre.Connected.Remove(connection);
                        }
                        else
                        {
                            if (destination.Connected.Count < 0)
                                return;
                            ConnectionCentre b = connection.Centre;
                            connection.Centre.Connected.Remove(connection);
                            JoinConnectionCentres(destination, connection.Centre);
                        }
                    }
                }
            }

            // Build list of connections
            Dictionary<Component, List<ConnectionReference>> t1 = new Dictionary<Component, List<ConnectionReference>>();

            List<UniqueConnectionDescription> allUniqueConnectionDescriptions = new List<UniqueConnectionDescription>();
            Dictionary<UniqueConnectionDescription, List<Connection>> connectRef = new Dictionary<UniqueConnectionDescription, List<Connection>>();
            foreach (Component component in document.Components)
            {
                Dictionary<ConnectionDescription, UniqueConnectionDescription> processed = new Dictionary<ConnectionDescription, UniqueConnectionDescription>();
                foreach (KeyValuePair<System.Windows.Point, Connection> connection in component.GetConnections())
                {
                    if (!processed.ContainsKey(connection.Value.Description))
                    {
                        UniqueConnectionDescription a = new UniqueConnectionDescription(component, connection.Value.Description);
                        processed.Add(connection.Value.Description, a);
                        allUniqueConnectionDescriptions.Add(a);
                        connectRef.Add(a, new List<Connection>());
                    }
                    connectRef[processed[connection.Value.Description]].Add(connection.Value);
                }
            }

            List<List<UniqueConnectionDescription>> collectionY = new List<List<UniqueConnectionDescription>>();
            foreach (UniqueConnectionDescription x in allUniqueConnectionDescriptions)
            {
                bool breakAll = false;

                foreach (List<UniqueConnectionDescription> y in collectionY)
                {
                    foreach (Connection connectionX in connectRef[x])
                    {
                        foreach (UniqueConnectionDescription y1 in y)
                        {
                            if (x == y1)
                                break;

                            foreach (Connection connectionY in connectRef[y1])
                            {
                                if (connectionY.IsConnected && connectionX.IsConnected && connectionY.Centre == connectionX.Centre)
                                {
                                    y.Add(x);
                                    breakAll = true;
                                    break;
                                }
                            }

                            if (breakAll)
                                break;
                        }

                        if (breakAll)
                            break;
                    }

                    if (breakAll)
                        break;
                }

                if (!breakAll)
                {
                    List<UniqueConnectionDescription> nl = new List<UniqueConnectionDescription>();
                    nl.Add(x);
                    collectionY.Add(nl);
                }
            }

            int namedConnectionRefCounter = 0;
            foreach (var item in collectionY)
            {
                foreach (var item2 in item)
                {
                    if (!t1.ContainsKey(item2.Component))
                        t1.Add(item2.Component, new List<ConnectionReference>());
                    if (item.Count > 1)
                        t1[item2.Component].Add(new ConnectionReference(namedConnectionRefCounter.ToString(), item2.ConnectionDescription.Name));
                }
                namedConnectionRefCounter++;
            }
            #endregion

            writer.WriteStartElement("elements");
            foreach (Component component in document.Components)
            {
                if (component.Description.ComponentName.ToLower() == "wire")
                {
                    writer.WriteStartElement("w");
                    writer.WriteAttributeString("x", component.Offset.X.ToString());
                    writer.WriteAttributeString("y", component.Offset.Y.ToString());
                    writer.WriteAttributeString("o", (component.Horizontal ? "h" : "v"));
                    writer.WriteAttributeString("sz", component.Size.ToString());
                    writer.WriteEndElement();

                    continue;
                }

                writer.WriteStartElement("c");
                Dictionary<string, object> properties = new Dictionary<string, object>();
                component.Serialize(properties);

                //writer.WriteAttributeString("id", componentIDs[component]); // currently not needed

                if (descriptionRefs[component.Description] == null)
                    writer.WriteAttributeIfContains(properties, "@type", "tp");
                else
                    writer.WriteAttributeString("tp", "{" + descriptionRefs[component.Description].InternalID + "}");

                // Layout
                if (includeLayout)
                {
                    writer.WriteAttributeString("x", component.Offset.X.ToString());
                    writer.WriteAttributeString("y", component.Offset.Y.ToString());
                    writer.WriteAttributeString("o", (component.Horizontal ? "h" : "v"));
                    if (component.Description.CanResize)
                        writer.WriteAttributeString("sz", component.Size.ToString());
                    if (component.Description.CanFlip)
                        writer.WriteAttributeString("flp", (component.IsFlipped ? "true" : "false"));
                }

                // properties
                if (properties.Count > 0)
                {
                    writer.WriteStartElement("prs");
                    foreach (ComponentConfiguration configuration in component.Description.Metadata.Configurations)
                        if (configuration.Matches(component) && !String.IsNullOrEmpty(configuration.ImplementationName))
                        {
                            writer.WriteAttributeString("cfg", configuration.Name);
                            break;
                        }
                    foreach (KeyValuePair<string, object> property in properties)
                    {
                        if (property.Key.StartsWith("@"))
                            continue;

                        writer.WriteStartElement("p");
                        writer.WriteAttributeString("k", property.Key.ToString());
                        writer.WriteAttributeString("v", property.Value.ToString());
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }

                if (includeConnections && t1.ContainsKey(component))
                {
                    // connections
                    writer.WriteStartElement("cns");
                    foreach (ConnectionReference reference in t1[component])
                    {
                        writer.WriteStartElement("cn");
                        writer.WriteAttributeString("id", reference.ID);
                        writer.WriteAttributeString("pt", reference.Point);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static void WriteAttributeIfContains(this XmlTextWriter writer, Dictionary<string, object> properties, string contains, string writeAs)
        {
            object value;
            if (properties.TryGetValue(contains, out value))
                writer.WriteAttributeString(writeAs, value.ToString());
        }

        class ComponentSource
        {
            public string InternalID { get; set; }
            public string ExternalID { get; set; }
            public string ComponentName { get; set; }
            public Guid GUID { get; set; }

            public string ImplementationName { get; set; }
            public List<ConfigurationImplementation> ConfigurationImplementations { get; private set; }

            public ComponentSource(string internalID, string externalID, string componentName, Guid guid)
            {
                InternalID = internalID;
                ExternalID = externalID;
                ComponentName = componentName;
                GUID = guid;
                ConfigurationImplementations = new List<ConfigurationImplementation>();
            }

            public class ConfigurationImplementation
            {
                public string ImplementationName { get; set; }
                public string ConfigurationName { get; set; }

                public ConfigurationImplementation(string implementationName, string configurationName)
                {
                    ImplementationName = implementationName;
                    ConfigurationName = configurationName;
                }

                public void WriteAsNode(XmlWriter writer)
                {
                    writer.WriteStartElement("cfg");
                    writer.WriteAttributeString("name", ConfigurationName);
                    writer.WriteAttributeString("impl", ImplementationName);
                    writer.WriteEndElement();
                }
            }
        }

        class ComponentSourceLocation
        {
            public string RelationshipID { get; private set; }
            public string DefinitionSource { get; private set; }
            public ComponentDescriptionSource OriginalSource { get; private set; }

            public ComponentSourceLocation(ComponentDescriptionSource originalSource, string relationshipID, string definitionSource)
            {
                OriginalSource = originalSource;
                RelationshipID = relationshipID;
                DefinitionSource = definitionSource;
            }

            public void Write(XmlTextWriter writer)
            {
                if (RelationshipID != null)
                    writer.WriteAttributeString("id", RelationshipNamespace, RelationshipID);
                if (!String.IsNullOrEmpty(DefinitionSource))
                    writer.WriteAttributeString("col", DefinitionSource);
            }
        }
    }
}
