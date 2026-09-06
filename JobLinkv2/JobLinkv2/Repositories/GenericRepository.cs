using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace JobLinkv2.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        IDbConnection connection;
        readonly string connectionString = "Server=(localdb)\\MSSQLLocalDB; Database=Joblinkv2; Trusted_Connection=true; MultipleActiveResultSets=true";
        public GenericRepository()
        {
            connection = new SqlConnection(connectionString);

        }
        public IEnumerable<T> GetAll()
        {
            string tableName = GetTableName();

            string query = $"SELECT * FROM {tableName} WHERE is_deleted = 0";

            return connection.Query<T>(query);
        }

        public T GetById(object id)
        {
            string tableName = GetTableName();

            var keyProp = GetKeyProperty();
            string keyColumn = GetKeyColumn();

            string query = $"SELECT * FROM {tableName} WHERE {keyColumn} = @{keyProp.Name} AND is_deleted = 0";

            var parameters = new Dictionary<string, object>
            {
                { keyProp.Name, id }
            };

            return connection.QueryFirstOrDefault<T>(query, parameters);
        }



        public bool Add(T Entity)
        {
            string tableName = GetTableName();
            string columns = GetColumnNames();
            string values = GetColumnValues();

            string query = $"INSERT INTO {tableName} ({columns}) VALUES ({values})";

            int affectedRow = 0;
            affectedRow = connection.Execute(query, Entity);
            return affectedRow == 1;
        }

        private PropertyInfo GetKeyProperty()
        {
            return typeof(T).GetProperties()
                .FirstOrDefault(p => Attribute.IsDefined(p, typeof(KeyAttribute)));
        }


        public bool Update(T entity)
        {
            string tableName = GetTableName();
            string setClause = GetSetClause();

            var keyProp = GetKeyProperty();
            string keyColumn = GetKeyColumn();

            string query = $"UPDATE {tableName} SET {setClause} WHERE {keyColumn} = @{keyProp.Name}";

            int affectedRow = connection.Execute(query, entity);
            return affectedRow == 1;
        }

        private string GetKeyColumn()
        {
            var keyProp = GetKeyProperty();

            var columnAttr = (ColumnAttribute)Attribute.GetCustomAttribute(keyProp, typeof(ColumnAttribute));

            return columnAttr != null ? columnAttr.Name : keyProp.Name;
        }



        public string GetSetClause()
        {
            var properties = typeof(T).GetProperties()
                .Where(p => !Attribute.IsDefined(p, typeof(KeyAttribute)));

            var setClause = string.Join(", ", properties.Select(p =>
            {
                var columnAttr = (ColumnAttribute)Attribute.GetCustomAttribute(p, typeof(ColumnAttribute));
                string columnName = columnAttr != null ? columnAttr.Name : p.Name;

                return $"{columnName} = @{p.Name}";
            }));

            return setClause;
        }


        public bool Delete(int id)
        {
            string tableName = GetTableName();

            string query = $"UPDATE {tableName} SET is_deleted = 1 WHERE user_id = @user_id";

            int affectedRow = connection.Execute(query, new { user_id = id });

            return affectedRow == 1;
        }

        public string GetTableName()
        {
            string tableName = "";
            var type = typeof(T);
            var tableAttr = type.GetCustomAttribute<TableAttribute>();
            if (tableAttr != null)
            {
                tableName = $"[{tableAttr.Name}]";
            }
            return tableName;
        }

        private string GetColumnNames()
        {
            var properties = typeof(T).GetProperties()
                .Where(p => !Attribute.IsDefined(p, typeof(KeyAttribute))); // skip PK

            return string.Join(", ", properties.Select(p =>
            {
                var colAttr = (ColumnAttribute)Attribute.GetCustomAttribute(p, typeof(ColumnAttribute));
                return colAttr != null ? colAttr.Name : p.Name;
            }));
        }


        public string GetColumnValues(bool excludeKey = true)
        {

            var columnValues = typeof(T).GetProperties()
                .Where(p => !excludeKey || p.GetCustomAttribute<KeyAttribute>() == null);
            var values = string.Join(",", columnValues.Select(p =>
            {
                return $"@{p.Name}";
            }));

            return values;
        }

        public bool Delete(object id)
        {
            string tableName = GetTableName();

            var keyProp = GetKeyProperty();
            string keyColumn = GetKeyColumn();

            string query = $"UPDATE {tableName} SET is_deleted = 1 WHERE {keyColumn} = @{keyProp.Name}";

            var parameters = new Dictionary<string, object>
            {
                { keyProp.Name, id }
            };

            int affectedRow = connection.Execute(query, parameters);

            return affectedRow == 1;
        }


    }
}
