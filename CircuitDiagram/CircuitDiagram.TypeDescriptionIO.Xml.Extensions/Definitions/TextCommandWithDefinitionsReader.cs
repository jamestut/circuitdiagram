﻿using CircuitDiagram.Drawing.Text;
using CircuitDiagram.TypeDescription;
using CircuitDiagram.TypeDescription.Conditions;
using CircuitDiagram.TypeDescriptionIO.Xml.Logging;
using CircuitDiagram.TypeDescriptionIO.Xml.Parsers.ComponentPoints;
using CircuitDiagram.TypeDescriptionIO.Xml.Readers.RenderCommands;
using CircuitDiagram.TypeDescriptionIO.Xml.Render;
using CircuitDiagram.TypeDescriptionIO.Xml.Sections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CircuitDiagram.TypeDescriptionIO.Xml.Extensions.Definitions
{
    class TextCommandWithDefinitionsReader : IRenderCommandReader
    {
        private static readonly Version TextRotationMinFormatVersion = new Version(1, 4);

        private readonly IXmlLoadLogger logger;
        private readonly IComponentPointParser componentPointParser;
        private readonly DefinitionsSection definitionsSection;

        public TextCommandWithDefinitionsReader(
            IXmlLoadLogger logger,
            IComponentPointParser componentPointParser,
            ISectionRegistry sectionRegistry)
        {
            this.logger = logger;
            this.componentPointParser = componentPointParser;
            definitionsSection = sectionRegistry.GetSection<DefinitionsSection>();
        }

        public bool ReadRenderCommand(XElement element, ComponentDescription description, out IXmlRenderCommand command)
        {
            var textCommand = new XmlRenderTextWithDefinitions();
            command = textCommand;

            if (!ReadTextLocation(element, textCommand))
            {
                return false;
            }

            if (!TryParseTextAlignment(element.Attribute("align"), out ConditionalCollection<TextAlignment> alignment))
            {
                return false;
            }
            textCommand.Alignment = alignment;

            var tRotation = "0";
            if (description.Metadata.FormatVersion >= TextRotationMinFormatVersion && element.Attribute("rotate") != null)
                tRotation = element.Attribute("rotate").Value;

            var rotation = TextRotation.None;
            switch (tRotation)
            {
                case "0":
                    rotation = TextRotation.None;
                    break;
                case "90":
                    rotation = TextRotation.Rotate90;
                    break;
                case "180":
                    rotation = TextRotation.Rotate180;
                    break;
                case "270":
                    rotation = TextRotation.Rotate270;
                    break;
                default:
                    logger.LogError(element.Attribute("rotate"), $"Invalid value for text rotation: '{tRotation}'");
                    break;
            }
            textCommand.Rotation = new ConditionalCollection<TextRotation>
            {
                new Conditional<TextRotation>(rotation, ConditionTree.Empty),
            };

            double size = 11d;
            if (element.Attribute("size") != null)
            {
                if (element.Attribute("size").Value.ToLowerInvariant() == "large")
                    size = 12d;
            }

            var textValueNode = element.Element(XmlLoader.ComponentNamespace + "value");
            if (textValueNode != null)
            {
                foreach (var spanNode in textValueNode.Elements())
                {
                    string nodeValue = spanNode.Value;
                    var formatting = TextRunFormatting.Normal;

                    if (spanNode.Name.LocalName == "sub")
                        formatting = TextRunFormatting.Subscript;
                    else if (spanNode.Name.LocalName == "sup")
                        formatting = TextRunFormatting.Superscript;
                    else if (spanNode.Name.LocalName != "span")
                        logger.LogWarning(spanNode, $"Unknown node '{spanNode.Name}' will be treated as <span>");

                    var textRun = new TextRun(nodeValue, formatting);

                    if (!ValidateText(element, description, textRun.Text))
                        return false;

                    textCommand.TextRuns.Add(textRun);
                }
            }
            else if (element.GetAttribute("value", logger, out var value))
            {
                var textRun = new TextRun(value.Value, new TextRunFormatting(TextRunFormattingType.Normal, size));

                if (!ValidateText(value, description, textRun.Text))
                    return false;

                textCommand.TextRuns.Add(textRun);
            }
            else
            {
                return false;
            }

            return true;
        }

        private bool TryParseTextAlignment(XAttribute alignmentAttribute, out ConditionalCollection<TextAlignment> alignment)
        {
            string tAlignment = "TopLeft";
            if (alignmentAttribute != null)
                tAlignment = alignmentAttribute.Value;

            if (!tAlignment.StartsWith("$"))
            {
                if (!Enum.TryParse(tAlignment, out TextAlignment singleAlignment))
                {
                    logger.LogError(alignmentAttribute, $"Invalid value for text alignment: '{tAlignment}'");
                    alignment = null;
                    return false;
                }

                alignment = new ConditionalCollection<TextAlignment>
                {
                    new Conditional<TextAlignment>(singleAlignment, ConditionTree.Empty),
                };
                return true;
            }
            else
            {
                // Check variable exists
                var variableName = tAlignment.Substring(1);
                if (!definitionsSection.Definitions.TryGetValue(variableName, out var variableValues))
                {
                    logger.LogError(alignmentAttribute, $"Variable '{tAlignment}' does not exist");
                    alignment = null;
                    return false;
                }

                // Check all possible values are valid
                alignment = new ConditionalCollection<TextAlignment>();
                foreach (var variableValue in variableValues)
                {
                    if (!Enum.TryParse(variableValue.Value, out TextAlignment parsedValue) || !Enum.IsDefined(typeof(TextAlignment), parsedValue))
                    {
                        logger.LogError(alignmentAttribute, $"Value '{variableValue.Value}' for ${variableName} is not a valid text alignment");
                        return false;
                    }

                    alignment.Add(new Conditional<TextAlignment>(parsedValue, variableValue.Conditions));
                }

                return true;
            }
        }

        private bool ValidateText(XAttribute attribute, ComponentDescription description, string text)
        {
            if (ValidateText(description, text, out var errorMessage))
                return true;

            logger.LogError(attribute, errorMessage);
            return false;
        }

        private bool ValidateText(XElement element, ComponentDescription description, string text)
        {
            if (ValidateText(description, text, out var errorMessage))
                return true;

            logger.LogError(element, errorMessage);
            return false;
        }

        protected virtual bool ValidateText(ComponentDescription description, string text, out string errorMessage)
        {
            if (!text.StartsWith("$"))
            {
                errorMessage = null;
                return true;
            }

            var propertyName = text.Substring(1);
            if (description.Properties.Any(x => x.Name == propertyName))
            {
                errorMessage = null;
                return true;
            }

            errorMessage = $"Property {propertyName} used for text value does not exist";
            return false;
        }

        protected virtual bool ReadTextLocation(XElement element, XmlRenderText command)
        {
            if (!element.GetAttribute("x", logger, out var x) ||
                !element.GetAttribute("y", logger, out var y))
                return false;

            if (!componentPointParser.TryParse(x, y, out var location))
                return false;
            command.Location = location;

            return true;
        }
    }
}
