using System.Data.SqlClient;

/* run 3rd after first two scrapers to add info to PlayingPeriod table */

using (SqlConnection connection = new SqlConnection("Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SaintsPlayerDb;Integrated Security=True;Connect Timeout=30;Encrypt=False;MultipleActiveResultSets=True;"))
{
    connection.Open();

    string selectQuery = "SELECT Id, [YearsAtClub] FROM Players";

    using (SqlCommand selectCommand = new SqlCommand(selectQuery, connection))
    {
        using (SqlDataReader reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                int playerId = reader.GetInt32(0);
                string yearsPlaying = reader.GetString(1);

                string[] yearRanges;

                if (yearsPlaying.Contains(','))
                {
                    yearRanges = yearsPlaying.Split(',');
                }
                else
                {
                    yearRanges = new string[] { yearsPlaying };
                }



                // Insert the data into the PlayerYearsPlayed table
                for (int i = 0; i < yearRanges.Length; i++)
                {
                    string[] years = yearRanges[i].Trim().Split('-');

                    if (years.Length == 2 && int.TryParse(years[0], out int yearFrom) && int.TryParse(years[1], out int yearTo))
                    {
                        string insertQuery = "INSERT INTO PlayingPeriod (PlayerId, YearFrom, YearTo) " +
                                             "VALUES (@PlayerId, @YearFrom, @YearTo)";

                        using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@PlayerId", playerId);

                            insertCommand.Parameters.AddWithValue("@YearFrom", yearFrom);
                            insertCommand.Parameters.AddWithValue("@YearTo", yearTo);

                            insertCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }

    connection.Close();
}