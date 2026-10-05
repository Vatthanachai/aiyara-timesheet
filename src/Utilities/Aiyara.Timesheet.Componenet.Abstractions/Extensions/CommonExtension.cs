using System.Collections;
using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class CommonExtension
{
    /// <summary>
    /// Validate object is null. If true is null, false not null
    /// </summary>
    /// <param name="value">Object for validate is null</param>
    /// <returns>true, false</returns>
    public static bool IsNullable(this object? value) => value == null;

    /// <summary>
    /// Validate string is null or empty. If true is null/empty. false is not null/not empty
    /// </summary>
    /// <param name="value">string value</param>
    /// <returns>true, false</returns>
    public static bool IsEmpty(this string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Validate guid is empty. If true is empty, false not empty.
    /// Empty guid value : '00000000-0000-0000-0000-000000000000'
    /// </summary>
    /// <param name="value">Guid data</param>
    /// <returns>true, false</returns>
    public static bool IsEmpty(this Guid value) => Guid.Empty == value;

    /// <summary>
    /// Convert string to Guid.
    /// </summary>
    /// <param name="value">string of guid</param>
    /// <returns></returns>
    public static Guid ToGuid(this string value)
    {
        if (value.IsEmpty())
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Guid.Parse(value);
    }

    /// <summary>
    /// Convert string to guid.
    /// </summary>
    /// <param name="value">Value for convert to Guid</param>
    /// <returns></returns>
    public static Guid? ToTryGuid(this string value)
    {
        bool isConvert = Guid.TryParse(value, out var newId);
        return isConvert ? newId : null;
    }

    /// <summary>
    /// Adds the elements of the specified collection to the end of the System.Collections.Generic.List`1.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="collection">
    /// The collection whose elements should be added to the end of the System.Collections.Generic.List`1.
    ///  The collection itself cannot be null, but it can contain elements that are null,
    ///  if type T is a reference type.
    /// </param>
    /// <returns></returns>
    public static ICollection<T> AddRange<T>(this ICollection<T> source, IEnumerable<T> collection)
    {
        if (collection.IsNullable())
        {
            throw new ArgumentNullException(nameof(collection));
        }

        return new List<T>(collection);
    }

    /// <summary>
    /// Get name from JsonPropertyNameAttribute with property name.
    /// </summary>
    /// <typeparam name="T">Class</typeparam>
    /// <param name="entity">Class</param>
    /// <param name="propertyName">Property name</param>
    /// <returns></returns>
    public static string? GetNameWithJsonPropertyNameAttribute<T>(this T entity, string? propertyName)
        where T : class
    {
        if (propertyName.IsEmpty())
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        var propertyInfo = typeof(T).GetProperties()
            .FirstOrDefault(f => f.Name == propertyName);
        if (propertyInfo.IsNullable())
        {
            return propertyName;
        }

        return GetNameWithJsonPropertyNameAttribute<T>(entity, propertyInfo);
    }

    /// <summary>
    /// Get name from JsonPropertyNameAttribute with property info.
    /// </summary>
    /// <typeparam name="T">Class</typeparam>
    /// <param name="entity">Class</param>
    /// <param name="propertyInfo">Property info</param>
    /// <returns></returns>
    public static string? GetNameWithJsonPropertyNameAttribute<T>(this T entity, PropertyInfo propertyInfo)
        where T : class
    {
        if (propertyInfo.IsNullable())
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        var customAttr = propertyInfo.GetCustomAttributes(true)
            .FirstOrDefault(f => f is JsonPropertyNameAttribute);
        if (customAttr.IsNullable())
        {
            return propertyInfo.Name;
        }

        try
        {
            var jsonPropertyName = customAttr as JsonPropertyNameAttribute;
            return jsonPropertyName?.Name;
        }
        catch
        {
            return propertyInfo.Name;
        }
    }

    /// <summary>
    /// Convert string to enum case-sensitive
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <returns></returns>
    public static T ToEnum<T>(this string value) where T : struct
        => value.ToEnum<T>(false);

    /// <summary>
    /// Convert object to enum case-sensitive
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <returns></returns>
    public static T ToEnum<T>(this object value) where T : struct
        => value.ToString().ToEnum<T>(false);

    /// <summary>
    /// Convert string to enum case-insensitive.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <param name="ignoreCase">true to ignore case; false to regard case.</param>
    /// <returns></returns>
    public static T ToEnum<T>(this string? value, bool ignoreCase) where T : struct
    {
        if (value.IsEmpty())
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Enum.Parse<T>(value, ignoreCase);
    }

    /// <summary>
    /// Convert object to enum case-insensitive.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <param name="ignoreCase">true to ignore case; false to regard case.</param>
    /// <returns></returns>
    public static T ToEnum<T>(this object value, bool ignoreCase) where T : struct
        => value.ToString().ToEnum<T>(ignoreCase);

    /// <summary>
    /// Convert string to enum case-sensitive. Return value is nullable.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <returns></returns>
    public static T? ToTryEnum<T>(this string value) where T : struct => value.ToTryEnum<T>(false);

    /// <summary>
    /// Convert object to enum case-sensitive. Return value is nullable.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <returns></returns>
    public static T? ToTryEnum<T>(this object value) where T : struct
        => value.ToString().ToTryEnum<T>();

    /// <summary>
    /// Convert string to enum case-insensitive. Return value is nullable.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <param name="ignoreCase">true to ignore case; false to regard case.</param>
    /// <returns></returns>
    public static T? ToTryEnum<T>(this string value, bool ignoreCase) where T : struct
    {
        bool isConvert = Enum.TryParse<T>(value, ignoreCase, out var newValue);
        return isConvert ? newValue : new T?();
    }

    /// <summary>
    /// Convert object to enum case-insensitive. Return value is nullable.
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="value">Value for convert to enum</param>
    /// <param name="ignoreCase">true to ignore case; false to regard case.</param>
    /// <returns></returns>
    public static T? ToTryEnum<T>(this object value, bool ignoreCase) where T : struct
        => value.ToString().ToTryEnum<T>(ignoreCase);

    /// <summary>
    /// Replace string value to snake case
    /// </summary>
    /// <param name="source">Source for replace</param>
    /// <returns></returns>
    public static string ToSnakeCase(this string source)
        => Regex.Replace(source, @"(\w)([A-Z])", "$1_$2", RegexOptions.None, TimeSpan.FromSeconds(10)).ToLower();

    /// <summary>
    /// Encode plain text to base64
    /// </summary>
    /// <param name="value">Plain text for encode</param>
    /// <returns></returns>
    public static string ToBase64Encode(this string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// Decode plain text to base64
    /// </summary>
    /// <param name="value">Format of base64</param>
    /// <returns></returns>
    public static string ToBase64Decode(this string value)
        => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    /// <summary>
    /// Validate environment for worker on local. If true is local, false is not local.
    /// </summary>
    /// <param name="hostEnvironment">IHostEnvironment</param>
    /// <returns></returns>
    public static bool IsLocal(this IHostEnvironment hostEnvironment)
        =>
            hostEnvironment.EnvironmentName.ToLower() == "local" || hostEnvironment.EnvironmentName.ToLower() == "Localhost";

    /// <summary>
    /// Validation type is collection. 
    /// Support type : <see cref="IEnumerable"/>, <see cref="ICollection"/>, <see cref="IList"/>, 
    /// <see cref="IEnumerable{T}"/>, <see cref="ICollection{T}"/>, <see cref="IList{T}"/>
    /// </summary>
    /// <param name="type">The type for validation.</param>
    /// <returns></returns>
    public static bool IsCollectionType(this Type type)
    {
        var collectionTypes = new[] { typeof(ICollection<>), typeof(IList<>), typeof(IEnumerable<>), typeof(ICollection), typeof(IList), typeof(IEnumerable) };
        return type.GetInterfaces().Any(t => collectionTypes.Contains(t));
    }

    /// <summary>
    /// Convert object to <seealso cref="DataTable"/>.
    /// </summary>
    /// <typeparam name="T">The type of data.</typeparam>
    /// <param name="data">The data for convert to DataTable.</param>
    /// <param name="tableName">The name of table.</param>
    /// <returns>
    /// Return data type is <see cref="DataTable"/>
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static DataTable ToDataTable<T>(this IEnumerable<T> data, string tableName = "") where T : class
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var properties = typeof(T).GetProperties();
        var results = new DataTable(tableName ?? typeof(T).Name);

        //Create columns.
        results.Columns.AddRange(properties.Select(s => new DataColumn(s.Name, typeof(object))).ToArray());

        foreach (var row in data)
        {
            var newRow = results.NewRow();
            foreach (var property in properties)
            {
                newRow[property.Name] = property.GetValue(row);
            }

            results.Rows.Add(newRow);
        }

        return results;
    }

    /// <summary>
    /// Convert <see cref="DateTime"/> to string format : yyyy-MM-dd HH:mm:ss.fffff. 
    /// Use culture : <see cref="System.Globalization.CultureInfo"/> of en-US.
    /// </summary>
    /// <param name="dateTime"></param>
    /// <returns></returns>
    public static string ToUSFullDateTimeString(this DateTime dateTime)
        => dateTime.ToString("yyyy-MM-dd HH:mm:ss.fffff", new CultureInfo("en-US"));

    /// <summary>
    /// Get name of <see cref="JsonPropertyNameAttribute"/> or
    /// <see cref="FromFormAttribute"/> or <see cref="FromQueryAttribute"/>.
    /// </summary>
    /// <typeparam name="TAttribute">
    /// Attribute type of <see cref="JsonPropertyNameAttribute"/> or 
    /// <see cref="FromFormAttribute"/> or <see cref="FromQueryAttribute"/>.
    /// </typeparam>
    /// <param name="propertyInfo">The property info for get name</param>
    /// <returns></returns>
    public static string GetNameFromContentTypeAttribute<TAttribute>(this PropertyInfo propertyInfo)
        where TAttribute : Attribute
    {
        var attr = propertyInfo.GetCustomAttribute<TAttribute>();
        if (attr.IsNullable())
        {
            return string.Empty;
        }

        switch (attr)
        {
            case JsonPropertyNameAttribute jsonPropertyName:
                return jsonPropertyName.Name;
            case FromFormAttribute formData:
                return formData.Name;
            case FromQueryAttribute query:
                return query.Name;
            case Newtonsoft.Json.JsonPropertyAttribute jsonProperty:
                return jsonProperty.PropertyName;
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Convert object to <see cref="IDictionary{TKey, TValue}"/>
    /// </summary>
    /// <param name="source">The source data</param>
    /// <returns></returns>
    public static IDictionary<string, object> ToDictionary(this object source)
        => source.ToDictionary<object>();

    /// <summary>
    /// Convert object to <see cref="IDictionary{TKey, TValue}"/>
    /// </summary>
    /// <typeparam name="T">Action for convert</typeparam>
    /// <param name="source">The source data</param>
    /// <returns></returns>
    public static IDictionary<string, T> ToDictionary<T>(this object source)
    {
        if (source.IsNullable())
        {
            throw new NullReferenceException("Unable to convert anonymous object to a dictionary. The source anonymous object is null.");
        }

        var dictionary = new Dictionary<string, T>();
        foreach (var property in source.GetType().GetProperties())
        {
            object value = property.GetValue(source);
            if (IsOfType<T>(value))
            {
                dictionary.Add(property.Name, (T)value);
            }
        }

        return dictionary;
    }

    /// <summary>
    /// Convert object to <see cref="IDictionary{TKey, TValue}"/> 
    /// and get name from <see cref="JsonPropertyNameAttribute"/> or
    /// <see cref="FromFormAttribute"/> or <see cref="FromQueryAttribute"/>.
    /// </summary>
    /// <typeparam name="TReturn">Action of value</typeparam>
    /// <typeparam name="TAttribute">
    /// Attribute type of <see cref="JsonPropertyNameAttribute"/> or 
    /// <see cref="FromFormAttribute"/> or <see cref="FromQueryAttribute"/>.
    /// </typeparam>
    /// <param name="source"></param>
    /// <returns></returns>
    public static IDictionary<string, TReturn> ToDictionary<TReturn, TAttribute>(this object source)
        where TAttribute : Attribute
    {
        if (source.IsNullable())
        {
            throw new NullReferenceException("Unable to convert anonymous object to a dictionary. The source anonymous object is null.");
        }

        var realTypes = new Type[] { typeof(int), typeof(long), typeof(short), typeof(decimal), typeof(float), typeof(double), typeof(bool), typeof(int?), typeof(long?), typeof(short?), typeof(decimal?), typeof(float?), typeof(double?), typeof(bool?) };

        var dictionary = new Dictionary<string, TReturn>();
        foreach (var property in source.GetType().GetProperties())
        {
            object valueProperty = property.GetValue(source);
            object newValue = null;

            string attrName = property.GetNameFromContentTypeAttribute<TAttribute>();
            if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            {
                newValue = valueProperty.IsNullable() ? null : Convert.ToDateTime(valueProperty).ToUSFullDateTimeString();
            }
            else if (realTypes.Any(t => t == property.PropertyType))
            {
                newValue = valueProperty.ToString();
            }
            else
            {
                newValue = (TReturn)valueProperty;
            }

            dictionary.Add(attrName.IsEmpty() ? property.Name : attrName, (TReturn)newValue);
        }

        return dictionary;
    }

    /// <summary>
    /// Validation <paramref name="data"/> is null or empty(count = 0).
    /// If <see langword="true" /> is null or empty, 
    /// but if <see langword="false" /> is not null and not empty.
    /// </summary>
    /// <typeparam name="T">The type of object.</typeparam>
    /// <param name="data">The data for validation.</param>
    /// <returns></returns>
    public static bool IsNullableOrEmpty<T>(this IEnumerable<T> data)
    {
        if (data.IsNullable())
        {
            return true;
        }

        return !data.Any();
    }

    /// <summary>
    /// Get error from exception
    /// </summary>
    /// <param name="ex"></param>
    /// <returns></returns>
    public static string GetErrorMessage(Exception ex)
    {
        string message = ex.Message;

        string innerMessage = string.Empty;
        if (!ex.InnerException.IsNullable())
        {
            innerMessage = " " + GetErrorMessage(ex.InnerException);
        }

        return $"{message}{innerMessage}";
    }

    /// <summary>
    /// Combine data to format xxx,xxx,xxx
    /// </summary>
    /// <param name="sources">sources data</param>
    /// <param name="symbol">symbol</param>
    /// <returns></returns>
    public static string CombineData(IEnumerable<string> sources, string symbol = ",")
        => CombineData(sources.ToList(), symbol);

    /// <summary>
    /// Combine data to format xxx,xxx,xxx
    /// </summary>
    /// <param name="sources">sources data</param>
    /// <param name="symbol">symbol</param>
    /// <returns></returns>
    public static string CombineData(List<string> sources, string symbol = ",")
    {
        string combineValues = string.Empty;
        sources.ForEach(f => { combineValues += (combineValues.IsEmpty() ? "" : symbol) + f; });

        return combineValues;
    }


    /// <summary>
    /// Get last id for move next data.
    /// </summary>
    /// <typeparam name="T">Class</typeparam>
    /// <param name="sources">List/Array of class</param>
    /// <param name="limit">limit</param>
    /// <param name="propertyIdName">Name of property id.</param>
    /// <returns></returns>
    public static string GetLastIdMoveNextData<T>(List<T> sources, int limit, string propertyIdName = "AuditId") where T : class
    {
        if (sources.IsNullable())
        {
            throw new ArgumentNullException(nameof(sources));
        }

        if (limit == 0 || (sources.Count == 0 && limit != sources.Count))
        {
            return string.Empty;
        }

        if (sources.Count == 0)
        {
            return string.Empty;
        }

        var propertyId = typeof(T).GetProperty(propertyIdName);
        if (propertyId.IsNullable())
        {
            throw new Exception($"Property name : '{propertyIdName}' not found");
        }

        var lastItem = sources.Last();
        if (lastItem.IsNullable())
        {
            return string.Empty;
        }

        var value = propertyId.GetValue(lastItem);
        return value.IsNullable() ? "" : value.ToString();
    }

    /// <summary>
    /// Get neutral culture info
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<CultureInfo> GetNeutralCultureInfos() => CultureInfo.GetCultures(CultureTypes.NeutralCultures);

    /// <summary>
    /// Get int value from <paramref name="types"/> by <paramref name="fieldNames"/>.
    /// The <see cref="Type"/> must be class only.
    /// </summary>
    /// <param name="types">The type of class for get value.</param>
    /// <param name="fieldNames">The field name for get value.</param>
    /// <returns></returns>
    public static List<int> GetIntValueFromFields(IEnumerable<Type> types, params string[] fieldNames)
    {
        if (types.IsNullable())
        {
            throw new ArgumentNullException(nameof(types));
        }

        if (fieldNames.IsNullable())
        {
            throw new ArgumentNullException(nameof(fieldNames));
        }

        if (fieldNames.Length == 0 || !types.Any())
        {
            return new List<int>();
        }

        //validate type must be class only.
        if (types.Any(t => !t.IsClass))
        {
            throw new Exception($"Parameter : '{nameof(types)}' must be class only.");
        }

        //validate field in multiple type of class
        var queryField = from f in fieldNames
                         where types.Select(t => t.GetField(f)).Count(c => !c.IsNullable()) > 1
                         select f;

        if (queryField.Any())
        {
            string fields = string.Join(", ", queryField);
            throw new Exception($"Cannot get value from field : '{fields}'. Because field in multiple of class.");
        }

        var errorIdList = new List<int>();
        foreach (var name in fieldNames)
        {
            object value = null;
            foreach (var type in types)
            {
                var field = type.GetField(name);
                if (field.IsNullable())
                {
                    continue;
                }

                value = field.GetValue(null);
            }

            if (!value.IsNullable())
            {
                errorIdList.Add(Convert.ToInt32(value));
            }
        }

        return errorIdList;
    }

    /// <summary>
    /// Get next page number 
    /// </summary>
    /// <param name="totalRow">The total row of result.</param>
    /// <param name="limit">The limit row</param>
    /// <param name="pageNumber">The current page number for get next page number.</param>
    /// <returns></returns>
    public static int GetNextPageNumber(int totalRow, int limit, int pageNumber) => totalRow < limit ? -1 : pageNumber + 1;


    /// <summary>
    /// Get type of variable.
    /// </summary>
    /// <param name="sourceType">The source type.</param>
    /// <returns>
    /// Return <see cref="Type"/>
    /// </returns>
    public static Type GetVariableType(Type sourceType)
        => sourceType.IsArray
            ? sourceType.GetElementType()
            : (sourceType.GenericTypeArguments.Length == 0
                ? sourceType
                : sourceType.GenericTypeArguments[0]);

    /// <summary>
    /// Compare type of <paramref name="value"/> and <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type for compare.</typeparam>
    /// <param name="value">The value for compare.</param>
    /// <returns></returns>
    public static bool IsOfType<T>(object value)
        => value is T;

    public static TModel GetOptions<TModel>(this IConfiguration? configuration, string section)
        where TModel : new()
    {
        TModel instance = new();
        configuration?.GetSection(section).Bind((object)instance);
        return instance;
    }

    public static bool IsValuePropertiesStringTypeEmpty(this object value)
    {
        var stringProperties = value.GetType().GetProperties()
            .Where(w => w.PropertyType == typeof(string));

        foreach (var item in stringProperties)
        {
            var propertyValue = item.GetValue(value);
            if (propertyValue.IsNullable())
            {
                return true;
            }
            else if (propertyValue.ToString().IsEmpty())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get data with page and page size
    /// </summary>
    /// <typeparam name="T">The type of entity</typeparam>
    /// <param name="list">Source data</param>
    /// <param name="limit">The limit of rows.</param>
    /// <param name="pageNumber">The page number for get next data.</param>
    /// <returns></returns>
    public static IQueryable<T> Paging<T>(this IQueryable<T> list, int limit, int pageNumber)
    {
        // A page size cannot be less than or equal to 0.
        if (limit < 1)
        {
            limit = 50; // Set to a default value.
        }

        // Clamp a page number because when it's converted into a page index, an index cannot be less than 0.
        if (pageNumber < 1)
        {
            pageNumber = 1; // Set to a default value.
        }

        var pageIndex = pageNumber - 1;
        var skip = limit * pageIndex;
        return list.Skip(skip).Take(limit);
    }

    /// <summary>
    /// Order data with descending and ascending.
    /// </summary>
    /// <typeparam name="T">Action of data</typeparam>
    /// <typeparam name="TKey">Key for order</typeparam>
    /// <param name="list">Source data</param>
    /// <param name="orderExpression">Expression for order by.</param>
    /// <param name="isDescendingOrder">If true is descending, else ascending.</param>
    /// <returns></returns>
    public static IQueryable<T> Order<T, TKey>(this IQueryable<T> list, Expression<Func<T, TKey>> orderExpression, bool isDescendingOrder = false)
        => !isDescendingOrder ? list.OrderByDescending(orderExpression) : list.OrderBy(orderExpression);

    /// <summary>
    /// This method to generate total pages for pagination
    /// </summary>
    /// <param name="totalRecord"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    public static int ToTotalPages(this int totalRecord, int pageSize)
    {
        return totalRecord > 0
            ? Convert.ToInt32(Math.Ceiling(totalRecord / Convert.ToDouble(pageSize)))
            : 0;
    }

    /// <summary>
    /// Get service. If you are not registered for the service is null.
    /// </summary>
    /// <param name="service"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public static object GetTryService(this IServiceProvider service, Type type)
    {
        try
        {
            return service.GetService(type);
        }
        catch
        {
            return default;
        }
    }


    /// <summary>
    /// Get value from 'appsettings.json' by section name.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="configuration"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static T GetValueBySection<T>(this IConfiguration configuration, string key)
    {
        object value = Activator.CreateInstance(typeof(T));
        configuration.GetSection(key).Bind(value);
        return (T)value;
    }

    /// <summary>
    /// Get name from <see cref="FromBodyAttribute"/>, <see cref="FromQueryAttribute"/>,
    /// <see cref="JsonPropertyNameAttribute"/>, <see cref="JsonProperty"/>.
    /// </summary>
    /// <typeparam name="TEntity">The type for get property</typeparam>
    /// <typeparam name="TFormAttribute">The attribute for get name</typeparam>
    /// <param name="entity">The entity for get type</param>
    /// <param name="propertyName">Name of property for get name</param>
    /// <returns></returns>
    public static string GetNameOfFormAttribute<TEntity, TFormAttribute>(this TEntity entity, string propertyName)
        where TFormAttribute : Attribute
    {
        var entityType = typeof(TEntity);
        var propertyInfo = entityType.GetProperty(propertyName);

        return propertyInfo.IsNullable()
            ? throw new Exception($"Property name : '{propertyName}' not found of : '{entityType.Name}'.")
            : propertyInfo.GetNameOfFormAttribute<TFormAttribute>();
    }


    /// <summary>
    /// Get name from <see cref="FromBodyAttribute"/>, <see cref="FromQueryAttribute"/>,
    /// <see cref="JsonPropertyNameAttribute"/>, <see cref="JsonProperty"/>.
    /// </summary>
    /// <typeparam name="TFormAttribute">The attribute for get name</typeparam>
    /// <param name="entity">The entity for get property</param>
    /// <param name="propertyName">The name of property</param>
    /// <returns></returns>
    public static string GetNameOfFormAttribute<TFormAttribute>(this object entity, string propertyName)
        where TFormAttribute : Attribute
    {
        var entityType = entity.GetType();
        var propertyInfo = entityType.GetProperty(propertyName);

        return propertyInfo.IsNullable()
            ? throw new Exception($"Property name : '{propertyName}' not found of : '{entityType.Name}'.")
            : GetNameOfFormAttribute<TFormAttribute>(propertyInfo: propertyInfo);
    }

    /// <summary>
    /// Get name from <see cref="FromBodyAttribute"/>, <see cref="FromQueryAttribute"/>,
    /// <see cref="JsonPropertyNameAttribute"/>, <see cref="JsonProperty"/>.
    /// </summary>
    /// <typeparam name="TFormAttribute">The attribute for get name</typeparam>
    /// <param name="propertyInfo">The property info</param>
    /// <returns></returns>
    public static string GetNameOfFormAttribute<TFormAttribute>(this PropertyInfo propertyInfo)
        where TFormAttribute : Attribute
    {
        if (propertyInfo.IsNullable())
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        var formAttribute = propertyInfo.GetCustomAttribute<TFormAttribute>();
        if (formAttribute.IsNullable())
        {
            return string.Empty;
        }

        var attrType = typeof(TFormAttribute);
        if (attrType == typeof(FromFormAttribute))
        {
            return (formAttribute as FromFormAttribute).Name;
        }
        else if (attrType == typeof(FromQueryAttribute))
        {
            return (formAttribute as FromQueryAttribute).Name;
        }
        else if (attrType == typeof(JsonPropertyNameAttribute))
        {
            return (formAttribute as JsonPropertyNameAttribute).Name;
        }
        else if (attrType == typeof(JsonPropertyAttribute))
        {
            return (formAttribute as JsonPropertyAttribute).PropertyName;
        }
        else
        {
            throw new Exception($"Function not support : '{attrType.Name}'.");
        }
    }


    public static List<string> ToList(this string value)
        => value.Split(",").ToList();

    public static string[] ToArray(this string value)
        => value.Split(",");


    /// <summary>
    /// Register(DI) application setting configs.
    /// </summary>
    /// <typeparam name="TAppSettings">Class of application setting</typeparam>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configuration">IConfiguration</param>
    /// <param name="sectionName">Section name</param>
    public static void RegisterAppSettings<TAppSettings>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TAppSettings : class
    {
        services.Configure<TAppSettings>(configuration.GetSection(sectionName));
    }

    /// <summary>
    /// Validation local. If true is local, false is not local.
    /// </summary>
    /// <param name="env">IWebHostEnvironment</param>
    /// <returns></returns>
    public static bool IsLocalhost(this IWebHostEnvironment env)
        => env.EnvironmentName.ToLower() is "localhost" or "local";

    /// <summary>
    /// Error exception path
    /// </summary>
    /// <param name="app"></param>
    /// <param name="env"></param>
    public static void ConfigureExceptionHandler(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsLocalhost() || env.IsStaging())
        {
            //app.UseExceptionHandler("/v1.0/error-local");
            app.UseExceptionHandler("/error");
        }
        /*
        else
        {
            app.UseExceptionHandler("/v1.0/error");
        }
        */
    }

    /// <summary>
    /// Convert byte array to hex string.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static string ToHexString(this byte[] data)
        => Convert.ToHexString(data);
}