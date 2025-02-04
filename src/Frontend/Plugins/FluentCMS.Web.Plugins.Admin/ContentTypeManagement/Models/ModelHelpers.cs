﻿using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public static class ModelHelpers
{

    private static string[] _reservedPropertyNames = ["Name", "Type", "Description", "Required", "Unique", "Settings", "Label"];

    public static IFieldValue GetFieldValue(this IFieldModel fieldModel, IDictionary<string, object?> valuesDict)
    {

        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
            case FieldTypes.SINGLE_SELECT:
                return new FieldValue<string?> { Name = fieldModel.Name, Value = valuesDict.TryGetValue(fieldModel.Name, out object? strValue) ? (string?)strValue : default! };

            case FieldTypes.NUMBER:
                return new FieldValue<double?> { Name = fieldModel.Name, Value = valuesDict.TryGetValue(fieldModel.Name, out object? doubleValue) ? (double?)doubleValue : default! };

            case FieldTypes.SINGLE_FILE:
                return new FieldValue<Guid?> { Name = fieldModel.Name, Value = valuesDict.TryGetValue(fieldModel.Name, out object? guidValue) ? (Guid?)guidValue : default! };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name, Value = valuesDict.TryGetValue(fieldModel.Name, out object? boolValue) && ((bool?)boolValue ?? false) };

            case FieldTypes.DATE_TIME:
                if (valuesDict[fieldModel.Name] is DateTime _dateTime)
                    return new FieldValue<DateTime?> { Name = fieldModel.Name, Value = _dateTime };

                // try parse the value as a DateTime
                if (valuesDict[fieldModel.Name] is string dateTimeStr && DateTime.TryParse(dateTimeStr, out DateTime dateTime))
                    return new FieldValue<DateTime?> { Name = fieldModel.Name, Value = dateTime };

                return new FieldValue<DateTime?> { Name = fieldModel.Name };

            case FieldTypes.MULTI_SELECT:
                return new FieldValue<ICollection<string>>
                {
                    Name = fieldModel.Name,
                    Value = (valuesDict[fieldModel.Name] as object[] ?? [])
                                .Select(x => x?.ToString() ?? string.Empty)
                                .ToList()
                };
            case FieldTypes.MULTI_FILE:
                valuesDict.TryGetValue(fieldModel.Name, out var value);
                return new FieldValue<ICollection<Guid>>
                {

                    Name = fieldModel.Name,
                    Value = (value as object[] ?? [])
                                .Select(x =>
                                {
                                    if (Guid.TryParse(x.ToString(), out var guid))
                                        return guid;

                                    return Guid.Empty;
                                })
                                .ToList()
                };

            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }
    }
    public static IFieldValue GetFieldValue(this IFieldModel fieldModel)
    {
        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
            case FieldTypes.SINGLE_SELECT:
                return new FieldValue<string?> { Name = fieldModel.Name };

            case FieldTypes.SINGLE_FILE:
                return new FieldValue<Guid?> { Name = fieldModel.Name };

            case FieldTypes.NUMBER:
                return new FieldValue<double?> { Name = fieldModel.Name };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name };

            case FieldTypes.DATE_TIME:
                return new FieldValue<DateTime?> { Name = fieldModel.Name };

            case FieldTypes.MULTI_SELECT:
                return new FieldValue<ICollection<string>> { Name = fieldModel.Name };

            case FieldTypes.MULTI_FILE:
                return new FieldValue<ICollection<Guid>> { Name = fieldModel.Name };

            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }
    }

    private static TField ToFieldModel<T, TField>(this ContentTypeField src) where TField : IFieldModel<T>, new()
    {
        TField result = new()
        {
            Name = src.Name!,
            Type = src.Type!,
            Description = src.Description!,
            Required = src.Required,
            Unique = src.Unique,
            Label = src.Label!,
        };

        PropertyInfo[] properties = typeof(TField).GetProperties();

        var settingsDict = src.Settings ?? [];

        foreach (PropertyInfo prop in properties)
        {
            if (_reservedPropertyNames.Contains(prop.Name))
                continue;

            // check if the property is writable, and if the settings dictionary contains a value for the property
            if (prop.CanWrite && settingsDict.ContainsKey(prop.Name))
            {
                var value = settingsDict[prop.Name];
                if (value == null)
                    continue;

                // TODO: Invalid cast error...
                var typedValue = Convert.ChangeType(value, prop.PropertyType);

                prop.SetValue(result, typedValue);
            }
        }

        return result;
    }

    public static IFieldModel ToFieldModel(this ContentTypeField src)
    {
        var typeName = src.Type ??
            throw new ArgumentNullException(nameof(src.Type));

        return typeName switch
        {
            FieldTypes.STRING => src.ToFieldModel<string?, StringFieldModel>(),
            FieldTypes.NUMBER => src.ToFieldModel<double?, NumberFieldModel>(),
            FieldTypes.BOOLEAN => src.ToFieldModel<bool, BooleanFieldModel>(),
            FieldTypes.DATE_TIME => src.ToFieldModel<DateTime?, DateFieldModel>(),
            FieldTypes.SINGLE_SELECT => src.ToFieldModel<string?, SelectFieldModel>(),
            FieldTypes.SINGLE_FILE => src.ToFieldModel<Guid?, SingleFileFieldModel>(),
            FieldTypes.MULTI_SELECT => src.ToFieldModel<ICollection<string>, MultiSelectFieldModel>(),
            FieldTypes.MULTI_FILE => src.ToFieldModel<ICollection<Guid>, MultiFileFieldModel>(),
            _ => throw new NotSupportedException($"Field type '{typeName}' is not supported."),
        };
    }

    public static ContentTypeField ToContentTypeField<T, TField>(this TField src) where TField : IFieldModel<T>
    {
        var result = new ContentTypeField
        {
            Name = src.Name,
            Type = src.Type,
            Description = src.Description,
            Required = src.Required,
            Unique = src.Unique,
            Label = src.Label,
            Settings = []
        };

        PropertyInfo[] properties = typeof(TField).GetProperties();

        foreach (PropertyInfo prop in properties)
        {
            if (_reservedPropertyNames.Contains(prop.Name))
                continue;

            result.Settings.Add(prop.Name, prop.GetValue(src));
        }

        return result;
    }
}
