﻿using System;
using System.Collections.Generic;

using System.Net;
using System.IO;
using CommonLibrary.Model.ServerSide;
using CommonLibrary.Implementation.ServerSide.Authentication;
using CommonLibrary.Implementation.ServerSide;
using CommonLibrary.Implementation.Common;
using System.Data;
using System.Data.SqlClient;

namespace HeadServer.DB
{
    /// <summary>
    /// Server initiates and remains connection to database
    /// Allows database operations (insert user/server, check if server is not dublicate, 
    /// selects list of all active servers)
    /// </summary>
    internal class DatabaseServer : IServer
    {
        public ServerStatus Status { get; internal set; }

        public IPEndPoint Socket { get; internal set; }
        public SqlConnection Connection { get; private set; }

        public DatabaseServer()
        {
            Status = ServerStatus.Uninitialized;
        }

        public event EventHandler OnInitialization;
        public event EventHandler OnTermination;
        public event EventHandler<ThreadStateEventArgs> OnThreadStateChange;

        public void Initialize()
        {
            _connection_string = GetConnectionString();
            Connection = new SqlConnection(_connection_string);

            Connection.Open(); //exceptions
            Status = ServerStatus.Initialized;
        }

        public void Initialize(IPEndPoint socket)
        {
            Socket = socket;

            Initialize();
        }

        public void Stop()
        {
            Status = ServerStatus.Stopped;
        }

        public void Start()
        {
            Status = ServerStatus.Running;
        }

        public void Resume()
        {
            Status = ServerStatus.Running;
        }

        public void Dispose()
        {
            Connection.Close();
            Connection.Dispose();
            Socket = null;

            Status = ServerStatus.Uninitialized;
        }

        public void ServerLoop()
        {
            
        }

        public bool InsertUser(UserEntry new_user)
        {
            using (SqlTransaction trans = Connection.BeginTransaction())
            using (SqlCommand insert_command = new SqlCommand("insert_user", Connection, trans) { CommandType = CommandType.StoredProcedure })
            {
                insert_command.Parameters.Add("@name", SqlDbType.NVarChar).Value = new_user.LoginName;
                insert_command.Parameters.Add("@passhash", SqlDbType.Binary).Value = new_user.PasswordHash;
                insert_command.Parameters.Add("@enroll_time", SqlDbType.DateTime).Value = DateTime.UtcNow;

                try
                {
                    int result_count = insert_command.ExecuteNonQuery();
                    if (result_count == 0)
                    {
                        trans.Rollback();
                        return false;
                    }
                    trans.Commit();
                    return true;
                }
                catch(SqlException e)
                {
                    return false;
                }

            }
        }

        public bool CheckUserLogin(string name, byte[] password_hash)
        {
            string command = "get_user_hash";
            using (SqlCommand cmd = new SqlCommand(command, Connection) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@name", SqlDbType.NVarChar).Value = name;

                SqlParameter param_hash = new SqlParameter("@hash", SqlDbType.Binary, 1024);
                cmd.Parameters.Add(param_hash).Direction = ParameterDirection.Output;

                cmd.ExecuteNonQuery();
                object ret = cmd.Parameters["@hash"].Value;
                /*if ((SqlDbType)ret == SqlDbType.DbNull)
                {

                } */
                if (ret is System.DBNull)
                {
                    return false;
                }
                byte[] hash = (byte[])cmd.Parameters["@hash"].Value;

                int fail_idx = -1;
                for (int i = 0; i < Math.Min(password_hash.Length, hash.Length); ++i)
                {
                    if (password_hash[i] != hash[i])
                    {
                        fail_idx = i;
                        break;
                    }
                }
                if (fail_idx == -1)
                {
                    return true;
                }
                bool pass_hash_zero = password_hash[fail_idx] == 0;
                bool hash_zero = hash[fail_idx] == 0;
                for (int i = fail_idx; i < Math.Min(password_hash.Length, hash.Length); ++i)
                {
                    if (pass_hash_zero)
                    {
                        if (password_hash[i] != 0)
                        {
                            return false;
                        }
                    }
                    else if (hash_zero) {
                        if (hash[i] != 0)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public List<GameServerEntry> SelectActiveGameServers()
        {
            string command = "SELECT * FROM game_servers WHERE is_active = 1";
            using (SqlCommand cmd = new SqlCommand(command, Connection))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<GameServerEntry> result = new List<GameServerEntry>();
                    int ord_name = reader.GetOrdinal("name");
                    int ord_ip = reader.GetOrdinal("ip");
                    int ord_port = reader.GetOrdinal("port");
                    int ord_max_players = reader.GetOrdinal("max_players");
                    while (reader.Read())
                    {
                        var game_serv_entry = new GameServerEntry();
                        game_serv_entry.Name = reader.GetString(ord_name);
                        game_serv_entry.Socket = new IPEndPoint(new IPAddress(reader.GetInt64(ord_ip)), reader.GetInt32(ord_port));
                        game_serv_entry.MaxPlayers = reader.GetInt16(ord_max_players);
                        game_serv_entry.IsActive = true;
                        result.Add(game_serv_entry);
                    }
                    return result;
                }
            }
            return null;
        }

        public bool SetServerIsActive(int server_id, bool is_active)
        {
            string command = "UPDATE game_servers SET is_active = " + (is_active ? 1 : 0) + " WHERE id = " + server_id;
            using (SqlTransaction trans = Connection.BeginTransaction())
            using (SqlCommand cmd = new SqlCommand(command, Connection, trans))
            {
                int result = cmd.ExecuteNonQuery();
                if (result > 0)
                {
                    trans.Commit();
                    return true;
                }
                trans.Rollback();
                return false;
            }
        }

        private static string GetConnectionString()
        {
            //return @"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=W:\@WORK\C#\LocalNetGame\Head-Server\Database-Server\SQL-Server-Classic\HeadDB.mdf;Integrated Security=True";
            // 1 Type of data source
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = @"(LocalDB)\MSSQLLocalDB";
            // 2 Location of source
            builder.AttachDBFilename = Path.GetFullPath(@"Database-Server\SQL-Server-Classic\HeadDB.mdf");
            //builder.
            // 3 Security substring
            builder.IntegratedSecurity = true;
            return builder.ConnectionString;
        }

        private string _connection_string;
    }
}
