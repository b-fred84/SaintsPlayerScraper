using HtmlAgilityPack;
using System.Data.SqlClient;
using SaintsPlayerScraper;
using Serilog;

/* First scraper, run to poputlate database.  
 * Is slow running,  look at ways to speed up process. 
 * Is it a big problem running slow given it only needs to be run twice a year 
 * after transfer windows close?*/

Log.Logger = (Serilog.ILogger)new LoggerConfiguration()
.WriteTo.File("SaintsPlayerScraperLog.txt")
.CreateLogger();

String url = "https://www.saintsplayers.co.uk/players/";
var doc = GetDocument(url);
List<string> links = GetPlayerLinks(url);
List<Player> players = GetPlayerInfo(links);


static List<Player> GetPlayerInfo(List<string> links)
{
    var players = new List<Player>();

    foreach (string link in links)
    {
        String url = $"{link}";
        var doc = GetDocument(url);
        var player = new Player();


        //add links to players
        player.link = link;

        //get names
        HtmlNode fullNameElement = doc.DocumentNode.SelectSingleNode("//h3[@class='subtitle']");
        string fullName = fullNameElement.LastChild.InnerText.Trim();

        string[] nameParts = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < nameParts.Length; i++)
        {
            string[] subParts = nameParts[i].Split('-');
            for (int j = 0; j < subParts.Length; j++)
            {
                string part = subParts[j].ToLower();
                part = char.ToUpper(part[0]) + part.Substring(1);
                subParts[j] = part;
            }
            nameParts[i] = string.Join("-", subParts);
        }
        fullName = string.Join(" ", nameParts);

        int firstSpaceIndex = fullName.IndexOf(' ');
        player.fullName = fullName;
        if (firstSpaceIndex >= 0)
        {
            player.firstName = fullName.Substring(0, firstSpaceIndex);
            player.lastName = fullName.Substring(firstSpaceIndex + 1);
        }
        else
        {
            player.firstName = fullName;
            player.lastName = "";
            Log.Information("No LastName found for {Name}", player.fullName);
        }

        //get appearances
        HtmlNode allAppearancesElement = doc.DocumentNode.SelectSingleNode("//p[@class='apps']");
        if (allAppearancesElement != null && allAppearancesElement.ChildNodes.Count > 0)
        {
            try
            {
                player.appearances = int.Parse(allAppearancesElement.ChildNodes[0].InnerText.Trim());
            }
            catch (FormatException e) 
            { 
                Log.Error(e, "Error parsing appearances for: {Name}. - Error Message: {ErrorMessage}", player.fullName, e.Message);
                player.appearances = null;
            }


            HtmlNode subElement = allAppearancesElement.SelectSingleNode("sub");

            if (subElement != null)
            {
                try
                {
                    player.appearancesAsSub = int.Parse(subElement.InnerText.Replace("Sub", "").Trim());
                }
                catch (FormatException e)
                {
                    Log.Error(e, "Error parsing appearances as sub for: {Name}. - Error Message: {ErrorMessage}", player.fullName, e.Message);
                    player.appearances = null;
                }
                
            }
            else
            {
                Log.Information("No AppearancesAsSub found for {Name}", player.fullName);
                player.appearancesAsSub = 0;
            }
        }
        else
        {
            Log.Information("No Appearances found for {Name}", player.fullName);
            player.appearances = 0;
        }

      

        //get years played
        HtmlNode yearsPlayerElement = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'yearsplayed')]");
        if (yearsPlayerElement != null)
        {
            var eachSpellAtClubElement = yearsPlayerElement.Descendants("p");

            List<string> yearsPlayed = new List<string>();
            List<string> yearsPlayedCleaned = new List<string>();
            foreach (var pElement in eachSpellAtClubElement)
            {
                yearsPlayed.Add(pElement.InnerText.Trim());
            }
            foreach (var element in yearsPlayed)
            {
                string yearsWithoutSymbols = "";
                for (int i = 0; i < element.Length; i++)
                {
                    if (Char.IsDigit(element[i]))
                        yearsWithoutSymbols += element[i];
                }
                if (yearsWithoutSymbols.Length >= 4)
                {
                    yearsWithoutSymbols = yearsWithoutSymbols.Insert(4, "-");
                }

                yearsPlayedCleaned.Add(yearsWithoutSymbols);

            }
            player.yearsPlaying = string.Join(", ", yearsPlayedCleaned);
            if (player.yearsPlaying.Length >= 4)
            {
                player.firstYearAtClub = player.yearsPlaying.Substring(0, 4);
            }
            else
            {
                Log.Information("No FirstYearAtClub found for: {Name}", player.fullName);
                player.firstYearAtClub = "unknown";
            }


        }
        else
        {
            Log.Information("No YearsPlaying found for: {Name}", player.fullName);
            player.yearsPlaying = "unknown";
        }


        //get goals
        HtmlNode divElement = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'column is-one-third-desktop')]/span[text()='GOALS']/..");
        if (divElement != null)
        {

            HtmlNode pElement = divElement.SelectSingleNode("p");

            if (pElement != null)
            {
                try
                {
                    player.goals = int.Parse(pElement.InnerText.Trim());
                }
                catch (FormatException e)
                {
                    Log.Error(e, "Error parsingt goals for: {Name}. - Error Message: {ErrorMessage}", player.fullName, e.Message);
                    player.goals = null;
                }

                

            }
        }
        else
        {
            Log.Information("No Goals found for: {Name}", player.fullName);
            player.goals = null;
        }


        //get DoB
        if (divElement != null)
        {
            HtmlNode dateOfBirthElement = doc.DocumentNode.SelectSingleNode("//div[@class='column is-two-thirds-desktop']/span[text()='DATE OF BIRTH']/following-sibling::p[1]");
            if (dateOfBirthElement != null)
            {
                string uneditedDoB = dateOfBirthElement.InnerText.Trim();
                player.yearOfBirth = uneditedDoB.Substring(uneditedDoB.Length - 4);

                if (IsValidDate(uneditedDoB))
                {
                    string[] DobArr = uneditedDoB.Split(" ");
                    //fixes 3 part date to add a zero if day is 1 digit, cuts month to 3 chars
                    if (DobArr.Length == 3)
                    {
                        if (DobArr[0].Length == 1)
                        {
                            DobArr[0] = "0" + DobArr[0];
                        }
                        if (DobArr[1].Length > 3)
                        {
                            DobArr[1] = DobArr[1].Substring(0, 3);
                        }
                        player.dateOfBirth = string.Join(" ", DobArr);
                    }
                    // handles if day and month or month and year are missing a seperation
                    else if (DobArr.Length == 2)
                    {
                        if (Char.IsDigit(DobArr[1], DobArr[1].Length - 4) && DobArr.Length > 4)
                        {
                            string month = DobArr[1].Substring(0, 3);
                            string year = DobArr[1].Substring(DobArr.Length - 4);
                            if (DobArr[0].Length == 1)
                            {
                                DobArr[0] = "0" + DobArr[0];
                            }
                            player.dateOfBirth = DobArr[0] + " " + month + " " + year;
                        }
                        else if (Char.IsDigit(DobArr[0][0]) && DobArr[0].Length > 2)
                        {
                            string day = "";
                            string month = "";
                            for (int i = 0; i < DobArr[0].Length; i++)
                            {
                                if (Char.IsLetter(DobArr[0][i]))
                                {
                                    day = DobArr[0].Substring(0, i);
                                    month = DobArr[0].Substring(i);
                                    break;
                                }

                            }
                            if (day.Length == 1)
                            {
                                day = "0" + day;
                            }
                            if (month.Length > 3)
                            {
                                month = month.Substring(0, 3);
                            }
                            player.dateOfBirth = day + " " + month + " " + DobArr[1];
                        }
                        else
                        {
                            player.dateOfBirth = uneditedDoB;
                        }
                    }

                }
                else
                {
                    Log.Information("No DateOfBirth found for {Name}", player.fullName);
                    player.dateOfBirth = "01 Jan 1111";
                }

            }
            else
            {
                Log.Information("No DateOfBirth found for {Name}", player.fullName);
                player.dateOfBirth = "01 Jan 1111";
                player.yearOfBirth = "unknown";
            }


        }
        else
        {
            Log.Information("No DateOfBirth found for {Name}", player.fullName);
            player.dateOfBirth = "01 Jan 1111";
            player.yearOfBirth = "unknown";
        }


        players.Add(player);
    }
    return players;
}



//html helper methods
static List<string> GetPlayerLinks(string url)
{
    var doc = GetDocument(url);

    var linkNodes = doc.DocumentNode.SelectNodes("//div[@class='columns is-multiline is-mobile commentlist']//div/a");
    var baseUri = new Uri(url);
    var links = new List<string>();
    foreach (var node in linkNodes)
    {
        var link = node.Attributes["href"].Value;
        // Console.WriteLine(link);

        links.Add(link);
    }
    return links;
}
static HtmlDocument GetDocument(string url)
{
    var web = new HtmlWeb();
    HtmlDocument doc = web.Load(url);
    return doc;
}

//checking date method
static bool IsValidDate(string dateString)
{
    DateTime date;
    return DateTime.TryParse(dateString, out date);
}


//add to database
string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SaintsPlayerDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;";

using (SqlConnection connection = new SqlConnection(connectionString))
{
    connection.Open();

    string insertQuery = "INSERT INTO [dbo].[Players] ([FullName], [FirstName], [LastName], [Appearances], [AppsAsSub], [Goals], [YearsAtClub], [Link], [DateOfBirth], [YearOfBirth], [FirstYearAtClub]) " +
                            "VALUES (@FullName, @FirstName, @LastName, @Appearances, @AppsAsSub, @Goals, @YearsAtClub, @Links, @DateOfBirth, @YearOfBirth, @FirstYearAtClub)";

    foreach (Player player in players)
    {
        try
        {
            using (SqlCommand command = new SqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@FullName", player.fullName);
                command.Parameters.AddWithValue("@FirstName", player.firstName);
                command.Parameters.AddWithValue("@LastName", player.lastName);
                command.Parameters.AddWithValue("@Appearances", player.appearances);
                command.Parameters.AddWithValue("@AppsAsSub", player.appearancesAsSub);
                command.Parameters.AddWithValue("@Goals", player.goals);
                command.Parameters.AddWithValue("@YearsAtClub", player.yearsPlaying);
                command.Parameters.AddWithValue("@Links", player.link);
                command.Parameters.AddWithValue("@DateOfBirth", player.dateOfBirth);
                command.Parameters.AddWithValue("@YearOfBirth", player.yearOfBirth);
                command.Parameters.AddWithValue("@FirstYearAtClub", player.firstYearAtClub);

                command.ExecuteNonQuery();
            }

        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occured when entering {Name} into the database.  Error Message: {ErrorMessage}", player.fullName, ex.Message);       
        }


    }
    connection.Close();

}

Log.CloseAndFlush();