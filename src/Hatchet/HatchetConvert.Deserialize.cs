﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hatchet.Extensions;

namespace Hatchet
{
    public static partial class HatchetConvert
    {
        internal const string ClassNameKey = "Class";

        public static T Deserialize<T>(string input)
        {
            var parser = new Parser();
            var result = parser.Parse(ref input);
            var type = typeof(T);
            return (T)DeserializeObject(result, type);
        }

        private static object DeserializeObject(object result, Type type)
        {
            var context = new DeserializationContext(result, type);

            var count = DeserializationRules.Count;
            for (var index = 0; index < count; index++)
            {
                var rule = DeserializationRules[index];
                if (rule.Item1(context))
                {
                    return rule.Item2(context);
                }
            }

            throw new HatchetException($"Unable to convert {result} - unknown type {type}");
        }

        private static bool IsGenericCollection(object result, Type type)
        {
            return result is ICollection && type.IsGenericType;
        }

        private static bool IsNullableValueType(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        private static bool IsComplexType(Type type)
        {
            return type.IsClass || type.IsValueType || type.IsInterface;
        }

        private static object DeserializeNullableValueType(DeserializationContext context)
        {
            var input = context.Input;
            var type = context.OutputType;
            
            if (input.ToString().Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            var actualValue = Convert.ChangeType(input, type.GenericTypeArguments[0]);
            var nullableValue = Activator.CreateInstance(type, actualValue);
            return nullableValue;
        }

        private static object DeserializeGenericCollection(DeserializationContext context)
        {
            var type = context.OutputType;
            var input = context.Input;
            
            var elementType = type.GenericTypeArguments[0];

            var setG = typeof(ISet<>).MakeGenericType(elementType);

            var inputList = (List<object>) input;

            if (setG.IsAssignableFrom(type))
            {
                return DeserializeHashSet(elementType, inputList);
            }

            var listG = typeof(List<>).MakeGenericType(elementType);
            if (type.IsAssignableFrom(listG))
            {
                return DeserializeList(elementType, inputList);
            }

            throw new HatchetException($"Unable to deserialize generic collection");
        }

        private static object DeserializeList(Type elementType, List<object> inputList)
        {
            var genericListType = typeof(List<>).MakeGenericType(elementType);
            var outputList = (IList) Activator.CreateInstance(genericListType);

            foreach (var inputItem in inputList)
            {
                outputList.Add(DeserializeObject(inputItem, elementType));
            }

            return outputList;
        }

        private static object DeserializeHashSet(Type elementType, List<object> inputList)
        {
            var genericListType = typeof(HashSet<>).MakeGenericType(elementType);
            var outputList = Activator.CreateInstance(genericListType);

            foreach (var inputItem in inputList)
            {
                genericListType.InvokeMember("Add", BindingFlags.InvokeMethod, null, outputList,
                    new[] {DeserializeObject(inputItem, elementType)});
            }

            return outputList;
        }

        private static bool IsSimpleValueType(Type type)
        {
            return type.IsPrimitive 
                   || type == typeof(decimal) 
                   || type == typeof(DateTime);
        }

        private static object DeserializeEnum(DeserializationContext context)
        {
            var input = context.Input;
            var type = context.OutputType;
            
            var rItems = input as ICollection;
            if (rItems != null)
            {
                if (rItems.Count == 0)
                    return 0;

                var items = rItems.Select(x => x.ToString());
                var enumStr = string.Join(",", items);
                return Enum.Parse(type, enumStr, true);
            }

            return Enum.Parse(type, (string) input, true);
        }

        private static object DeserializeDictionary(DeserializationContext context)
        {
            var inputDictionary = (IDictionary) context.Input;
            var outputDictionary = (IDictionary) Activator.CreateInstance(context.OutputType);

            var outputGta = outputDictionary.GetType().GetGenericArguments();
            var outputKeyType = outputGta[0];
            var outputValueType = outputGta[1];

            // todo: skip this process if the input and output dictionary generic types match

            // go through each input dictionary key and convert to the output key and value.
            foreach (var key in inputDictionary.Keys)
            {
                var newKeyValue = DeserializeObject(key, outputKeyType);

                var value = inputDictionary[key];
                var newValue = DeserializeObject(value, outputValueType);

                outputDictionary[newKeyValue] = newValue;
            }

            return outputDictionary;
        }

        private static object DeserializeArray(DeserializationContext context)
        {
            var arrayType = context.OutputType.GetElementType();
            var inputList = (List<object>) context.Input;
            var outputArray = Array.CreateInstance(arrayType, inputList.Count);

            for (var i = 0; i < inputList.Count; i++)
            {
                outputArray.SetValue(DeserializeObject(inputList[i], arrayType), i);
            }

            return outputArray;
        }

        private static object DeserializeComplexType(DeserializationContext context)
        {
            var input = context.Input;
            var type = context.OutputType;
            
            if (input is string)
            {
                var ctorMethod = ObjectFactory.FindStaticConstructorMethodWithSingleStringParameter(type);

                if (ctorMethod != null)
                {
                    return ctorMethod.Invoke(null, new[] {input});
                }
                
                var ctor = FindConstructorWithSingleStringParameter(type);

                if (ctor != null)
                {
                    return ctor.Invoke(new[] { input });
                }

                throw new HatchetException($"Can't convert {input} to {type}"); 
            }

            var inputValues = (Dictionary<string, object>) input;

            var newtype = FindComplexType(type, inputValues);

            var instance = ObjectFactory.CreateComplexType(newtype, inputValues);
            SetComplexTypeFields(newtype, inputValues, instance);
            SetComplexTypeProperties(newtype, inputValues, instance);

            return instance;
        }
        
        private static object DeserializeGuid(DeserializationContext context)
        {
            return new Guid(context.Input.ToString());
        }

        private static object DeserializeSimpleValue(DeserializationContext context)
        {
            return Convert.ChangeType(context.Input, context.OutputType);
        }

        private static ConstructorInfo FindConstructorWithSingleStringParameter(Type type)
        {
            var ctor = type.GetConstructors()
                .SingleOrDefault(x =>
                {
                    var pc = x.GetParameters();
                    if (pc.Length != 1)
                        return false;

                    return pc[0].ParameterType == typeof(string);
                });
            return ctor;
        }

        private static Type FindComplexType(Type type, Dictionary<string, object> inputValues)
        {
            if (inputValues.ContainsKey(ClassNameKey))
            {
                var name = inputValues[ClassNameKey].ToString();
                type = HatchetTypeRegistry.GetType(name);

                if (type == null)
                {
                    throw new HatchetException($"Can't create type - Type is not registered `{name}`");
                }
            }
            return type;
        }

        private static void SetComplexTypeProperties(
            Type type, 
            Dictionary<string, object> inputValues, 
            object output)
        {
            var props = GetWritablePropertiesForType(type);

            var propsCount = props.Length;
            for (var index = 0; index < propsCount; index++)
            {
                var prop = props[index];
                var propName = prop.Name;
                
                if (!inputValues.ContainsKey(propName))
                    continue;

                var value = inputValues[propName];
                prop.SetValue(output, DeserializeObject(value, prop.PropertyType));
            }
        }

        private static readonly Dictionary<Type, PropertyInfo[]> PropertyInfoCache =
            new Dictionary<Type, PropertyInfo[]>();

        private static PropertyInfo[] GetWritablePropertiesForType(Type type)
        {
            if (PropertyInfoCache.TryGetValue(type, out var propertyInfo))
                return propertyInfo;

            propertyInfo = type
                .GetProperties()
                .Where(x => x.CanWrite && !x.HasAttribute<HatchetIgnoreAttribute>())
                .ToArray();

            PropertyInfoCache[type] = propertyInfo;

            return propertyInfo;
        }
        
        private static readonly Dictionary<Type, FieldInfo[]> FieldInfoCache =
            new Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] GetFieldsForType(Type type)
        {
            if (FieldInfoCache.TryGetValue(type, out var fieldInfo))
                return fieldInfo;

            fieldInfo = type.GetFields()
                .Where(x => !x.HasAttribute<HatchetIgnoreAttribute>())
                .ToArray();

            FieldInfoCache[type] = fieldInfo;

            return fieldInfo;
        }
        
        private static void SetComplexTypeFields(
            Type type, 
            Dictionary<string, object> inputValues, 
            object output)
        {
            var fields = GetFieldsForType(type);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                var fieldName = field.Name;

                if (!inputValues.ContainsKey(fieldName))
                    continue;

                var value = inputValues[fieldName];
                field.SetValue(output, DeserializeObject(value, field.FieldType));
            }
        }
    }
}