using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hatchet.Extensions;
using Hatchet.Reflection;

namespace Hatchet
{
    internal class Serializer
    {
        private const string LineEnding = "\n";
        private const int IndentCount = 2;
        
        private PrettyPrinter PrettyPrinter { get; }
        private StringBuilder StringBuilder { get; }
        private SerializeOptions SerializeOptions { get; }

        private int IndentLevel => PrettyPrinter.IndentLevel;
        
        private readonly List<object> _metObjects;

        public Serializer(
            PrettyPrinter prettyPrinter, 
            StringBuilder stringBuilder, 
            SerializeOptions serializeOptions)
        {
            PrettyPrinter = prettyPrinter;
            StringBuilder = stringBuilder;
            SerializeOptions = serializeOptions;
            _metObjects = new List<object>();
        }

        internal void StaticSerialize(object input, bool forceClassName = false)
        {
            PushObjectRef(input);
            
            var context = new SerializationContext(input, this, forceClassName);

            var prettyPrinter = context.Serializer.PrettyPrinter;
            
            switch (input)
            {
                case Array arrayInput when arrayInput.GetType().IsArray:
                    SerializeArray(arrayInput, context);
                    break;
                case IDictionary dictionaryInput:
                    SerializeDictionary(dictionaryInput, context);
                    break;
                case object genericEnumerable when genericEnumerable.GetType().GenericTypeArguments.Length == 1:
                    SerializeGenericEnumerable(genericEnumerable, context);
                    break;
                case string strInput:
                    SerializeString(strInput, prettyPrinter);
                    break;
                case DateTime dateTimeInput:
                    SerializeDateTime(dateTimeInput, prettyPrinter);
                    break;
                case bool boolInput:
                    SerializeBoolean(boolInput, prettyPrinter);
                    break;
                case object simpleValue when IsSimpleValue(simpleValue.GetType()):
                    SerializeSimpleValue(simpleValue, prettyPrinter);
                    break;
                case ICollection collectionInput:
                    SerializeCollection(collectionInput, context);
                    break;
                case Enum enumValue when enumValue.GetType().IsEnum:
                    SerializeEnum(enumValue, prettyPrinter);
                    break;
                case object classOrStruct when classOrStruct.GetType().IsClass || classOrStruct.GetType().IsValueType:
                    SerializeClassOrStruct(classOrStruct, context);
                    break;
                default:
                    throw new HatchetException($"Could not serialize {input} of type {input.GetType()}");
            }
            
            PopObjectRef(input);
        }
        
        private static bool IsSimpleValue(Type inputType)
        {
            return inputType.IsPrimitive 
                   || inputType == typeof(decimal) 
                   || inputType == typeof(DateTime)
                   || inputType == typeof(Guid);
        }
        
        private void SerializeClassOrStruct(object input, SerializationContext context)
        {   
            var prettyPrinter = context.Serializer;

            var inputType = input.GetType();

            var customOutputValue = inputType
                .GetNonIgnoredProperties()
                .SingleOrDefault(x => x.HasAttribute<HatchetValueAttribute>());

            if (customOutputValue != null)
            {
                var value = customOutputValue.GetValue(input);
                prettyPrinter.Append(value);
                return;
            }
            
            prettyPrinter.AppendOpenBlock();
            if (context.ForceClassName)
            {
                WriteClassName(prettyPrinter, inputType);
            }
            SerializeFieldsAndProperties(context);
            prettyPrinter.AppendCloseBlock();
        }
        
        private void SerializeFieldsAndProperties(SerializationContext context)
        {
            var propertiesAndFields = GetPropertiesAndFields(context.Input);

            foreach (var member in propertiesAndFields)
            {
                SerializeMember(member);
            }
        }
        
        private void SerializeMember(ISerializableMember member)
        {
            SerializeKeyValue(member.Name, member.Value, member.IsValueAbstract);
        }
        
        private static void WriteClassName(Serializer serializer, Type inputType)
        {
            serializer.Append(' ', serializer.IndentLevel * IndentCount);
            serializer.Append(' ', IndentCount);
            serializer.AppendFormat("Class {0}", inputType.Name);
            serializer.Append(LineEnding);
        }

        private void SerializeKeyValue(string key, 
            object value, bool forceClassName = false)
        {
            if (value == null)
                return;
            
            if (key.Contains(" "))
            {
                throw new HatchetException(
                    $"`{key}` is an invalid key. Key cannot contain spaces.");
            }
            
            var type = value.GetType();
            
            if (type.IsValueType)
            {
                var comparable = Activator.CreateInstance(type);
                if (value.Equals(comparable) && !SerializeOptions.IncludeDefaultValues)
                    return;
            }
            
            Append(' ', IndentLevel * IndentCount);
            Append(' ', IndentCount);
            Append(key);
            Append(' ');
            IndentAndSerialize(value, forceClassName);
            Append(LineEnding);
        }

        private void IndentAndSerialize(object value, bool forceClassName)
        {
            Indent();
            StaticSerialize(value, forceClassName);
            Deindent();
        }
        
        private static IEnumerable<ISerializableMember> GetPropertiesAndFields(object input)
        {
            var inputType = input.GetType();
            
            foreach (var property in inputType.GetPropertiesToSerialize())
            {
                yield return new SerializableProperty(property, input);
            }
            
            foreach (var field in inputType.GetFieldsToSerialize())
            {
                yield return new SerializableField(field, input);
            }
        }
        
        private static void SerializeEnum(object value, PrettyPrinter prettyPrinter)
        {
            prettyPrinter.AppendEnum(value);
        }
        
        private static void SerializeSimpleValue(object input, PrettyPrinter prettyPrinter)
        {
            prettyPrinter.Append(input);
        }
        
        private void SerializeGenericEnumerable(object input, SerializationContext context)
        {
            var prettyPrinter = context.Serializer;
            var forceClassName = context.ForceClassName;

            var elementType = input.GetType().GenericTypeArguments[0];

            if (elementType.IsAbstract)
                forceClassName = true;

            var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);

            prettyPrinter.Append("[");

            if (enumerableType.IsInstanceOfType(input))
            {
                var enumerator = ((IEnumerable) input).GetEnumerator();

                var addSpace = false;
                while (enumerator.MoveNext())
                {
                    if (addSpace)
                        prettyPrinter.AppendFormat(" ");
                    addSpace = true;

                    var element = enumerator.Current;
                    IndentAndSerialize(element, forceClassName);
                }
            }

            prettyPrinter.Append("]");
        }
        
        private void SerializeDictionary(IDictionary input, SerializationContext context)
        {
            var serializer = context.Serializer;
            var prettyPrinter = serializer.PrettyPrinter;

            if (input.Count == 0)
            {
                prettyPrinter.Append("{}");
                return;
            }

            prettyPrinter.AppendOpenBlock();
            foreach (var key in input.Keys)
            {
                SerializeKeyValue(key.ToString(), input[key]);
            }
            prettyPrinter.AppendCloseBlock();
        }
        
        private void SerializeCollection(IEnumerable collectionInput, SerializationContext context)
        {
            var forceClassName = context.ForceClassName;

            foreach (var item in collectionInput)
            {
                IndentAndSerialize(item, forceClassName);
            }
        }
        
        private static void SerializeArray(Array inputArray, SerializationContext context)
        {
            var values = inputArray.Select(x => HatchetConvert.Serialize(x, context.Serializer.SerializeOptions));
            
            context.Serializer.AppendFormat("[{0}]", string.Join(" ", 
                values));
        }

        private static void SerializeString(string input, PrettyPrinter prettyPrinter)
        {
            prettyPrinter.AppendString(input);
        }

        private static void SerializeDateTime(DateTime input, PrettyPrinter prettyPrinter)
        {
            prettyPrinter.AppendDateTime(input);
        }

        private static void SerializeBoolean(bool input, PrettyPrinter prettyPrinter)
        {
            prettyPrinter.Append(input ? "true" : "false");
        }

        private void PushObjectRef(object obj)
        {
            var type = obj.GetType();

            if (obj is string)
                return;
            
            if (type.IsValueType)
                return;
            
            if (_metObjects.Contains(obj))
                throw new CircularReferenceException(obj);
            _metObjects.Add(obj);
        }

        private void PopObjectRef(object obj)
        {
            _metObjects.Remove(obj);
        }

        private void AppendFormat(string str, params object[] args)
        {
            PrettyPrinter.AppendFormat(str, args);
        }

        private void Indent()
        {
            PrettyPrinter.Indent();
        }

        private void Deindent()
        {
            PrettyPrinter.Deindent();
        }

        private void Append(string str)
        {
            PrettyPrinter.Append(str);
        }

        private void Append(char chr, int count)
        {
            PrettyPrinter.Append(chr, count);
        }

        private void Append(char chr)
        {
            PrettyPrinter.Append(chr);
        }

        private void Append(object obj)
        {
            PrettyPrinter.Append(obj);
        }

        private void AppendOpenBlock()
        {
            PrettyPrinter.AppendOpenBlock();
        }

        private void AppendCloseBlock()
        {
            PrettyPrinter.AppendCloseBlock();
        }
    }
}