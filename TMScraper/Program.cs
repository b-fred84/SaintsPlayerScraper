using SaintsPlayerScraper;
using System;
using HtmlAgilityPack;
using System.Data.SqlClient;
using System.Threading;
using System.Xml.Linq;
using System.Globalization;
using System.Xml;
using Serilog;
using System.Numerics;

/* second scraper to be run after SaintsPlayerScraper.  
   Uses player names and dob to verify correct players before 
   adding extra data to db */

Log.Logger = (Serilog.ILogger)new LoggerConfiguration()
.WriteTo.File("TMScraperLog.txt")
.CreateLogger();

List<(string Name, string dateOfBirthInDb)> namesList = new List<(string, string)>();
List<string> playersNotFoundOnTransferMarket = new List<string>();

string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SaintsPlayerDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;";

string query = "SELECT * FROM [dbo].[Players]";



// Create a connection to the database
using (SqlConnection connection = new SqlConnection(connectionString))
{
    connection.Open();

    using (SqlCommand command = new SqlCommand(query, connection))
    {
        using (SqlDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string dateOfBirthInDb = reader.GetString(14);
                string name = reader.GetString(1);
                name = name.Replace(" ", "+");
                namesList.Add((name, dateOfBirthInDb));
            }
        }
    }

    string baseUrl = "https://www.transfermarkt.co.uk/";

    foreach ((string playerName, string dateOfBirthInDb) in namesList)
    {
        string searchUrl = baseUrl + "schnellsuche/ergebnis/schnellsuche?query=" + playerName;

        
        HtmlWeb web = new HtmlWeb();
        HtmlDocument searchPage = web.Load(searchUrl);

        HtmlNode playerLink = searchPage.DocumentNode.SelectSingleNode("//td[@class='hauptlink']/a");
        if (playerLink == null)
        {
            Log.Information("Could not find player matching details for: {Name}", playerName);
            continue;
        }

        string playerUrl = playerLink.GetAttributeValue("href", "");
        string playerId = playerUrl.Substring(playerUrl.LastIndexOf('/') + 1);

        string playerPageUrl = "https://www.transfermarkt.co.uk" + playerUrl;

        HtmlDocument playerPage = web.Load(playerPageUrl);


        //Get date of birth
        HtmlNode dateOfBirthNode = playerPage.DocumentNode.SelectSingleNode("//span[@class='info-table__content info-table__content--bold']/a");
        string dateOfBirthOnTm = dateOfBirthNode?.InnerText.Trim();

        if (dateOfBirthOnTm != null)
        {
            try
            {
                string[] partsOfDoB = dateOfBirthOnTm.Split(' ');
                if (partsOfDoB[1].Length == 2)
                {
                    partsOfDoB[1] = "0" + partsOfDoB[1];
                }
                dateOfBirthOnTm = string.Join(" ", partsOfDoB);

                DateTime dobDb = DateTime.ParseExact(dateOfBirthInDb, "dd MMM yyyy", CultureInfo.InvariantCulture);
                DateTime dobTm = DateTime.ParseExact(dateOfBirthOnTm, "MMM dd, yyyy", CultureInfo.InvariantCulture);

                if (dobDb.Date == dobTm.Date)
                {
                    HtmlNode positionLabelElement = playerPage.DocumentNode.SelectSingleNode("//span[@class='info-table__content info-table__content--regular' and contains(text(), 'Position:')]");
                    string positionFull = "";
                    string title = "";

                    if (positionLabelElement != null)
                    {
                        HtmlNode positionElement = positionLabelElement.SelectSingleNode("./following-sibling::span[@class='info-table__content info-table__content--bold']");
                        positionFull = positionElement?.InnerText.Trim();

                    }

                    string[] positionArray = positionFull.Split(" ");
                    string position = positionArray[0];
                  
                    HtmlNode nationalityElement = playerPage.DocumentNode.SelectSingleNode("//span[@itemprop='nationality']/img[@class='flaggenrahmen']");
                    string nationality = nationalityElement?.GetAttributeValue("title", "");
                    
                    string correctName = playerName.Replace("+", " "); ;

                    //update database
                    string updateQuery = "UPDATE [dbo].[Players] SET Position = @position, [PositionFull] = @fullPosition, Nationality = @nationality WHERE [FullName] = @fullName AND [DateOfBirth] = @dateOfBirth";
                    try
                    {
                        using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@position", position);
                            updateCommand.Parameters.AddWithValue("@nationality", nationality);
                            updateCommand.Parameters.AddWithValue("@fullPosition", positionFull);
                            updateCommand.Parameters.AddWithValue("@fullName", correctName);
                            updateCommand.Parameters.AddWithValue("@dateOfBirth", dateOfBirthInDb);


                            updateCommand.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "An error occured when updating info for: {Name}. - Error Message: {ErrorMessage}", playerName, ex.Message);
                    }



                }
            }
            catch (FormatException ex)
            {
                Log.Error(ex, "Failed to parse the date of bitrth for: {Name}. - Error Message: {ErrorMessage}", playerName, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occured when handling data for: {Name}. - Error Message: {ErrorMessage}", playerName, ex.Message);
            }

        }
        else
        {
            Log.Information("Failed to scrape a matching date of birth for: {Name}", playerName);
        }


    }

    
}