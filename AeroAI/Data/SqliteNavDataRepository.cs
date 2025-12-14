using System;
using System.Collections.Generic;
using System.Linq;
using AeroAI.Models;
using Microsoft.Data.Sqlite;

namespace AeroAI.Data;

public sealed class SqliteNavDataRepository : INavDataRepository
{
	private readonly string _connectionString;

	public SqliteNavDataRepository(string databasePath)
	{
		if (string.IsNullOrWhiteSpace(databasePath))
		{
			throw new ArgumentException("Database path must be provided.", "databasePath");
		}
		SqliteConnectionStringBuilder sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadOnly,
			Cache = SqliteCacheMode.Shared
		};
		_connectionString = sqliteConnectionStringBuilder.ToString();
	}

	public IReadOnlyList<NavRunwaySummary> GetRunways(string airportIcao)
	{
		List<NavRunwaySummary> list = new List<NavRunwaySummary>();
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "\r\n            SELECT\r\n                airport_identifier,\r\n                runway_identifier,\r\n                runway_length,\r\n                runway_true_bearing\r\n            FROM tbl_runways\r\n            WHERE airport_identifier = $icao;";
			sqliteCommand.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			int ordinal = sqliteDataReader.GetOrdinal("airport_identifier");
			int ordinal2 = sqliteDataReader.GetOrdinal("runway_identifier");
			int ordinal3 = sqliteDataReader.GetOrdinal("runway_length");
			int ordinal4 = sqliteDataReader.GetOrdinal("runway_true_bearing");
			while (sqliteDataReader.Read())
			{
				string airportIcao2 = (sqliteDataReader.IsDBNull(ordinal) ? string.Empty : sqliteDataReader.GetString(ordinal));
				string runwayIdentifier = (sqliteDataReader.IsDBNull(ordinal2) ? string.Empty : sqliteDataReader.GetString(ordinal2));
				int lengthFeet = ((!sqliteDataReader.IsDBNull(ordinal3)) ? sqliteDataReader.GetInt32(ordinal3) : 0);
				int trueHeadingDegrees = ((!sqliteDataReader.IsDBNull(ordinal4)) ? sqliteDataReader.GetInt32(ordinal4) : 0);
				list.Add(new NavRunwaySummary
				{
					AirportIcao = airportIcao2,
					RunwayIdentifier = runwayIdentifier,
					LengthFeet = lengthFeet,
					TrueHeadingDegrees = trueHeadingDegrees,
					HasIlsOrLocalizer = false,
					HasRnavApproach = false,
					IsPreferredDeparture = false,
					IsPreferredArrival = false
				});
			}
		}
		try
		{
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "\r\n                SELECT DISTINCT\r\n                    runway_identifier,\r\n                    approach_type_identifier\r\n                FROM tbl_iaps\r\n                WHERE airport_identifier = $icao\r\n                  AND runway_identifier IS NOT NULL\r\n                  AND runway_identifier != '';";
			sqliteCommand2.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			if (sqliteDataReader2.FieldCount < 2)
			{
				return list;
			}
			int ordinal5;
			int ordinal6;
			try
			{
				ordinal5 = sqliteDataReader2.GetOrdinal("runway_identifier");
				ordinal6 = sqliteDataReader2.GetOrdinal("approach_type_identifier");
			}
			catch
			{
				return list;
			}
			while (sqliteDataReader2.Read())
			{
				string rwyId = (sqliteDataReader2.IsDBNull(ordinal5) ? string.Empty : sqliteDataReader2.GetString(ordinal5));
				string text = (sqliteDataReader2.IsDBNull(ordinal6) ? string.Empty : sqliteDataReader2.GetString(ordinal6));
				if (string.IsNullOrWhiteSpace(rwyId))
				{
					continue;
				}
				NavRunwaySummary? navRunwaySummary = list.FirstOrDefault((NavRunwaySummary r) => string.Equals(r.RunwayIdentifier, rwyId, StringComparison.OrdinalIgnoreCase));
				if (navRunwaySummary != null)
				{
					string text2 = text.Trim().ToUpperInvariant();
					bool flag;
					switch (text2)
					{
					case "I":
					case "L":
					case "G":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					bool flag2 = flag;
					flag = ((text2 == "R" || text2 == "E") ? true : false);
					bool flag3 = flag;
					if (flag2)
					{
						int index = list.IndexOf(navRunwaySummary);
						list[index] = new NavRunwaySummary
						{
							AirportIcao = navRunwaySummary.AirportIcao,
							RunwayIdentifier = navRunwaySummary.RunwayIdentifier,
							LengthFeet = navRunwaySummary.LengthFeet,
							TrueHeadingDegrees = navRunwaySummary.TrueHeadingDegrees,
							HasIlsOrLocalizer = true,
							HasRnavApproach = navRunwaySummary.HasRnavApproach,
							IsPreferredDeparture = navRunwaySummary.IsPreferredDeparture,
							IsPreferredArrival = navRunwaySummary.IsPreferredArrival
						};
					}
					if (flag3)
					{
						int index2 = list.IndexOf(navRunwaySummary);
						list[index2] = new NavRunwaySummary
						{
							AirportIcao = navRunwaySummary.AirportIcao,
							RunwayIdentifier = navRunwaySummary.RunwayIdentifier,
							LengthFeet = navRunwaySummary.LengthFeet,
							TrueHeadingDegrees = navRunwaySummary.TrueHeadingDegrees,
							HasIlsOrLocalizer = navRunwaySummary.HasIlsOrLocalizer,
							HasRnavApproach = true,
							IsPreferredDeparture = navRunwaySummary.IsPreferredDeparture,
							IsPreferredArrival = navRunwaySummary.IsPreferredArrival
						};
					}
				}
			}
		}
		catch
		{
			return list;
		}
		return list;
	}

	public IReadOnlyList<SidSummary> GetSids(string airportIcao)
	{
		List<SidSummary> list = new List<SidSummary>();
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		
		// Debug: Check if tbl_sids exists
		try
		{
			using var checkCmd = sqliteConnection.CreateCommand();
			checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '%sid%';";
			using var checkReader = checkCmd.ExecuteReader();
			var sidTables = new List<string>();
			while (checkReader.Read())
			{
				sidTables.Add(checkReader.GetString(0));
			}
			if (sidTables.Count == 0)
			{
				Console.WriteLine($"[NavData] No SID tables found in database");
			}
			else
			{
				Console.WriteLine($"[NavData] SID-related tables: {string.Join(", ", sidTables)}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[NavData] Error checking tables: {ex.Message}");
		}
		
		try
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n            SELECT DISTINCT\r\n                airport_identifier,\r\n                procedure_identifier,\r\n                route_type,\r\n                COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                transition_identifier\r\n            FROM tbl_sids\r\n            WHERE airport_identifier = $icao;";
			sqliteCommand.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			int ordinal;
			int ordinal2;
			int ordinal3;
			int ordinal4;
			int ordinal5;
			try
			{
				ordinal = sqliteDataReader.GetOrdinal("airport_identifier");
				ordinal2 = sqliteDataReader.GetOrdinal("procedure_identifier");
				ordinal3 = sqliteDataReader.GetOrdinal("route_type");
				ordinal4 = sqliteDataReader.GetOrdinal("runway_id");
				ordinal5 = sqliteDataReader.GetOrdinal("transition_identifier");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[NavData] SID column error: {ex.Message}");
				return list;
			}
			while (sqliteDataReader.Read())
			{
				string airportIcao2 = (sqliteDataReader.IsDBNull(ordinal) ? string.Empty : sqliteDataReader.GetString(ordinal));
				string procedureIdentifier = (sqliteDataReader.IsDBNull(ordinal2) ? string.Empty : sqliteDataReader.GetString(ordinal2));
				string routeType = (sqliteDataReader.IsDBNull(ordinal3) ? string.Empty : sqliteDataReader.GetString(ordinal3));
				string runwayIdentifier = (sqliteDataReader.IsDBNull(ordinal4) ? string.Empty : sqliteDataReader.GetString(ordinal4));
				string transitionIdentifier = (sqliteDataReader.IsDBNull(ordinal5) ? string.Empty : sqliteDataReader.GetString(ordinal5));
				list.Add(new SidSummary
				{
					AirportIcao = airportIcao2,
					ProcedureIdentifier = procedureIdentifier,
					RouteType = routeType,
					RunwayIdentifier = runwayIdentifier,
					TransitionIdentifier = transitionIdentifier,
					ExitFixIdentifier = string.Empty
				});
			}
			Console.WriteLine($"[NavData] Found {list.Count} SIDs for {airportIcao}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[NavData] SID query exception: {ex.Message}");
			return list;
		}
		try
		{
			using SqliteCommand sqliteCommand2 = new SqliteCommand("\r\n                SELECT\r\n                    procedure_identifier,\r\n                    route_type,\r\n                    COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                    transition_identifier,\r\n                    waypoint_identifier,\r\n                    seqno\r\n                FROM tbl_sids\r\n                WHERE airport_identifier = $icao;", sqliteConnection);
			sqliteCommand2.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			int ordinal6;
			int ordinal7;
			int ordinal8;
			int ordinal9;
			int ordinal10;
			int ordinal11;
			try
			{
				ordinal6 = sqliteDataReader2.GetOrdinal("procedure_identifier");
				ordinal7 = sqliteDataReader2.GetOrdinal("route_type");
				ordinal8 = sqliteDataReader2.GetOrdinal("runway_id");
				ordinal9 = sqliteDataReader2.GetOrdinal("transition_identifier");
				ordinal10 = sqliteDataReader2.GetOrdinal("waypoint_identifier");
				ordinal11 = sqliteDataReader2.GetOrdinal("seqno");
			}
			catch
			{
				return list;
			}
			Dictionary<(string, string, string, string), (int, string)> dictionary = new Dictionary<(string, string, string, string), (int, string)>();
			while (sqliteDataReader2.Read())
			{
				string item = (sqliteDataReader2.IsDBNull(ordinal6) ? string.Empty : sqliteDataReader2.GetString(ordinal6));
				string item2 = (sqliteDataReader2.IsDBNull(ordinal7) ? string.Empty : sqliteDataReader2.GetString(ordinal7));
				string item3 = (sqliteDataReader2.IsDBNull(ordinal8) ? string.Empty : sqliteDataReader2.GetString(ordinal8));
				string item4 = (sqliteDataReader2.IsDBNull(ordinal9) ? string.Empty : sqliteDataReader2.GetString(ordinal9));
				string item5 = (sqliteDataReader2.IsDBNull(ordinal10) ? string.Empty : sqliteDataReader2.GetString(ordinal10));
				int num = ((!sqliteDataReader2.IsDBNull(ordinal11)) ? sqliteDataReader2.GetInt32(ordinal11) : 0);
				(string, string, string, string) key = (item, item2, item3, item4);
				if (!dictionary.TryGetValue(key, out var value) || num > value.Item1)
				{
					dictionary[key] = (num, item5);
				}
			}
			for (int i = 0; i < list.Count; i++)
			{
				SidSummary sidSummary = list[i];
				(string, string, string, string) key2 = (sidSummary.ProcedureIdentifier, sidSummary.RouteType, sidSummary.RunwayIdentifier, sidSummary.TransitionIdentifier);
				if (dictionary.TryGetValue(key2, out var value2) && !string.IsNullOrWhiteSpace(value2.Item2))
				{
					list[i] = new SidSummary
					{
						AirportIcao = sidSummary.AirportIcao,
						ProcedureIdentifier = sidSummary.ProcedureIdentifier,
						RouteType = sidSummary.RouteType,
						RunwayIdentifier = sidSummary.RunwayIdentifier,
						TransitionIdentifier = sidSummary.TransitionIdentifier,
						ExitFixIdentifier = value2.Item2
					};
				}
			}
		}
		catch
		{
			return list;
		}
		return list;
	}

	public IReadOnlyList<StarSummary> GetStars(string airportIcao)
	{
		List<StarSummary> list = new List<StarSummary>();
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		try
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n            SELECT DISTINCT\r\n                airport_identifier,\r\n                procedure_identifier,\r\n                route_type,\r\n                COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                transition_identifier\r\n            FROM tbl_stars\r\n            WHERE airport_identifier = $icao;";
			sqliteCommand.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			int ordinal;
			int ordinal2;
			int ordinal3;
			int ordinal4;
			int ordinal5;
			try
			{
				ordinal = sqliteDataReader.GetOrdinal("airport_identifier");
				ordinal2 = sqliteDataReader.GetOrdinal("procedure_identifier");
				ordinal3 = sqliteDataReader.GetOrdinal("route_type");
				ordinal4 = sqliteDataReader.GetOrdinal("runway_id");
				ordinal5 = sqliteDataReader.GetOrdinal("transition_identifier");
			}
			catch
			{
				return list;
			}
			while (sqliteDataReader.Read())
			{
				string airportIcao2 = (sqliteDataReader.IsDBNull(ordinal) ? string.Empty : sqliteDataReader.GetString(ordinal));
				string procedureIdentifier = (sqliteDataReader.IsDBNull(ordinal2) ? string.Empty : sqliteDataReader.GetString(ordinal2));
				string routeType = (sqliteDataReader.IsDBNull(ordinal3) ? string.Empty : sqliteDataReader.GetString(ordinal3));
				string runwayIdentifier = (sqliteDataReader.IsDBNull(ordinal4) ? string.Empty : sqliteDataReader.GetString(ordinal4));
				string transitionIdentifier = (sqliteDataReader.IsDBNull(ordinal5) ? string.Empty : sqliteDataReader.GetString(ordinal5));
				list.Add(new StarSummary
				{
					AirportIcao = airportIcao2,
					ProcedureIdentifier = procedureIdentifier,
					RouteType = routeType,
					RunwayIdentifier = runwayIdentifier,
					TransitionIdentifier = transitionIdentifier,
					EntryFixIdentifier = string.Empty,
					ExitFixIdentifier = string.Empty
				});
			}
		}
		catch
		{
			return list;
		}
		try
		{
			using SqliteCommand sqliteCommand2 = new SqliteCommand("\r\n                SELECT\r\n                    procedure_identifier,\r\n                    route_type,\r\n                    COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                    transition_identifier,\r\n                    waypoint_identifier,\r\n                    seqno\r\n                FROM tbl_stars\r\n                WHERE airport_identifier = $icao;", sqliteConnection);
			sqliteCommand2.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			int ordinal6;
			int ordinal7;
			int ordinal8;
			int ordinal9;
			int ordinal10;
			int ordinal11;
			try
			{
				ordinal6 = sqliteDataReader2.GetOrdinal("procedure_identifier");
				ordinal7 = sqliteDataReader2.GetOrdinal("route_type");
				ordinal8 = sqliteDataReader2.GetOrdinal("runway_id");
				ordinal9 = sqliteDataReader2.GetOrdinal("transition_identifier");
				ordinal10 = sqliteDataReader2.GetOrdinal("waypoint_identifier");
				ordinal11 = sqliteDataReader2.GetOrdinal("seqno");
			}
			catch
			{
				return list;
			}
			Dictionary<(string, string, string, string), (int, string)> dictionary = new Dictionary<(string, string, string, string), (int, string)>();
			Dictionary<(string, string, string, string), (int, string)> dictionary2 = new Dictionary<(string, string, string, string), (int, string)>();
			while (sqliteDataReader2.Read())
			{
				string item = (sqliteDataReader2.IsDBNull(ordinal6) ? string.Empty : sqliteDataReader2.GetString(ordinal6));
				string item2 = (sqliteDataReader2.IsDBNull(ordinal7) ? string.Empty : sqliteDataReader2.GetString(ordinal7));
				string item3 = (sqliteDataReader2.IsDBNull(ordinal8) ? string.Empty : sqliteDataReader2.GetString(ordinal8));
				string item4 = (sqliteDataReader2.IsDBNull(ordinal9) ? string.Empty : sqliteDataReader2.GetString(ordinal9));
				string item5 = (sqliteDataReader2.IsDBNull(ordinal10) ? string.Empty : sqliteDataReader2.GetString(ordinal10));
				int num = ((!sqliteDataReader2.IsDBNull(ordinal11)) ? sqliteDataReader2.GetInt32(ordinal11) : 0);
				(string, string, string, string) key = (item, item2, item3, item4);
				if (!dictionary.TryGetValue(key, out var value) || num < value.Item1)
				{
					dictionary[key] = (num, item5);
				}
				if (!dictionary2.TryGetValue(key, out var value2) || num > value2.Item1)
				{
					dictionary2[key] = (num, item5);
				}
			}
			for (int i = 0; i < list.Count; i++)
			{
				StarSummary starSummary = list[i];
				(string, string, string, string) key2 = (starSummary.ProcedureIdentifier, starSummary.RouteType, starSummary.RunwayIdentifier, starSummary.TransitionIdentifier);
				dictionary.TryGetValue(key2, out var value3);
				dictionary2.TryGetValue(key2, out var value4);
				list[i] = new StarSummary
				{
					AirportIcao = starSummary.AirportIcao,
					ProcedureIdentifier = starSummary.ProcedureIdentifier,
					RouteType = starSummary.RouteType,
					RunwayIdentifier = starSummary.RunwayIdentifier,
					TransitionIdentifier = starSummary.TransitionIdentifier,
					EntryFixIdentifier = value3.Item2,
					ExitFixIdentifier = value4.Item2
				};
			}
		}
		catch
		{
			return list;
		}
		return list;
	}

	public IReadOnlyList<ApproachSummary> GetApproaches(string airportIcao)
	{
		List<ApproachSummary> list = new List<ApproachSummary>();
		using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		try
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n            SELECT DISTINCT\r\n                airport_identifier,\r\n                procedure_identifier,\r\n                route_type,\r\n                COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                approach_type_identifier\r\n            FROM tbl_iaps\r\n            WHERE airport_identifier = $icao;";
			sqliteCommand.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			int ordinal;
			int ordinal2;
			int ordinal3;
			int ordinal4;
			int ordinal5;
			try
			{
				ordinal = sqliteDataReader.GetOrdinal("airport_identifier");
				ordinal2 = sqliteDataReader.GetOrdinal("procedure_identifier");
				ordinal3 = sqliteDataReader.GetOrdinal("route_type");
				ordinal4 = sqliteDataReader.GetOrdinal("runway_id");
				ordinal5 = sqliteDataReader.GetOrdinal("approach_type_identifier");
			}
			catch
			{
				return list;
			}
			while (sqliteDataReader.Read())
			{
				string airportIcao2 = (sqliteDataReader.IsDBNull(ordinal) ? string.Empty : sqliteDataReader.GetString(ordinal));
				string procedureIdentifier = (sqliteDataReader.IsDBNull(ordinal2) ? string.Empty : sqliteDataReader.GetString(ordinal2));
				string routeType = (sqliteDataReader.IsDBNull(ordinal3) ? string.Empty : sqliteDataReader.GetString(ordinal3));
				string runwayIdentifier = (sqliteDataReader.IsDBNull(ordinal4) ? string.Empty : sqliteDataReader.GetString(ordinal4));
				string text = (sqliteDataReader.IsDBNull(ordinal5) ? string.Empty : sqliteDataReader.GetString(ordinal5));
				string text2 = text.Trim().ToUpperInvariant();
				bool flag = ((text2 == "I" || text2 == "G") ? true : false);
				bool hasGlideslope = flag;
				flag = ((text2 == "R" || text2 == "E") ? true : false);
				bool isRnav = flag;
				bool flag2 = text2 == "V";
				list.Add(new ApproachSummary
				{
					AirportIcao = airportIcao2,
					ProcedureIdentifier = procedureIdentifier,
					RouteType = routeType,
					RunwayIdentifier = runwayIdentifier,
					ApproachTypeCode = text2,
					HasGlideslope = hasGlideslope,
					IsRnav = isRnav,
					IsCirclingOnly = flag2,
					SupportsStraightIn = !flag2,
					InitialApproachFixIdentifier = string.Empty,
					FinalApproachFixIdentifier = string.Empty
				});
			}
		}
		catch
		{
			return list;
		}
		try
		{
			using SqliteCommand sqliteCommand2 = new SqliteCommand("\r\n                SELECT\r\n                    procedure_identifier,\r\n                    route_type,\r\n                    COALESCE(runway_identifier, runway_designator, '') as runway_id,\r\n                    transition_identifier,\r\n                    waypoint_identifier,\r\n                    seqno\r\n                FROM tbl_iaps\r\n                WHERE airport_identifier = $icao;", sqliteConnection);
			sqliteCommand2.Parameters.AddWithValue("$icao", airportIcao);
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			int ordinal6;
			int ordinal7;
			int ordinal8;
			int ordinal10;
			int ordinal11;
			try
			{
				ordinal6 = sqliteDataReader2.GetOrdinal("procedure_identifier");
				ordinal7 = sqliteDataReader2.GetOrdinal("route_type");
				ordinal8 = sqliteDataReader2.GetOrdinal("runway_id");
				int ordinal9 = sqliteDataReader2.GetOrdinal("transition_identifier");
				ordinal10 = sqliteDataReader2.GetOrdinal("waypoint_identifier");
				ordinal11 = sqliteDataReader2.GetOrdinal("seqno");
			}
			catch
			{
				return list;
			}
			Dictionary<(string, string, string), (int, string)> dictionary = new Dictionary<(string, string, string), (int, string)>();
			Dictionary<(string, string, string), (int, string)> dictionary2 = new Dictionary<(string, string, string), (int, string)>();
			while (sqliteDataReader2.Read())
			{
				string item = (sqliteDataReader2.IsDBNull(ordinal6) ? string.Empty : sqliteDataReader2.GetString(ordinal6));
				string item2 = (sqliteDataReader2.IsDBNull(ordinal7) ? string.Empty : sqliteDataReader2.GetString(ordinal7));
				string item3 = (sqliteDataReader2.IsDBNull(ordinal8) ? string.Empty : sqliteDataReader2.GetString(ordinal8));
				string item4 = (sqliteDataReader2.IsDBNull(ordinal10) ? string.Empty : sqliteDataReader2.GetString(ordinal10));
				int num = ((!sqliteDataReader2.IsDBNull(ordinal11)) ? sqliteDataReader2.GetInt32(ordinal11) : 0);
				(string, string, string) key = (item, item2, item3);
				if (!dictionary.TryGetValue(key, out var value) || num < value.Item1)
				{
					dictionary[key] = (num, item4);
				}
				if (!dictionary2.TryGetValue(key, out var value2) || num > value2.Item1)
				{
					dictionary2[key] = (num, item4);
				}
			}
			for (int i = 0; i < list.Count; i++)
			{
				ApproachSummary approachSummary = list[i];
				(string, string, string) key2 = (approachSummary.ProcedureIdentifier, approachSummary.RouteType, approachSummary.RunwayIdentifier);
				dictionary.TryGetValue(key2, out var value3);
				dictionary2.TryGetValue(key2, out var value4);
				list[i] = new ApproachSummary
				{
					AirportIcao = approachSummary.AirportIcao,
					ProcedureIdentifier = approachSummary.ProcedureIdentifier,
					RouteType = approachSummary.RouteType,
					RunwayIdentifier = approachSummary.RunwayIdentifier,
					ApproachTypeCode = approachSummary.ApproachTypeCode,
					HasGlideslope = approachSummary.HasGlideslope,
					IsRnav = approachSummary.IsRnav,
					IsCirclingOnly = approachSummary.IsCirclingOnly,
					SupportsStraightIn = approachSummary.SupportsStraightIn,
					InitialApproachFixIdentifier = value3.Item2,
					FinalApproachFixIdentifier = value4.Item2
				};
			}
		}
		catch
		{
			return list;
		}
		return list;
	}
}
