using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;

IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

OpenAIClient client = new OpenAIClient(new Uri(config.GetValue<string>("oaiEndpoint")), new Azure.AzureKeyCredential(config.GetValue<string>("oaiKey")));

var options = new ChatCompletionsOptions();
options.Messages.Add(new ChatMessage(ChatRole.System, "You are an AI Assistant to help answer use questions. Only use the functions to answer questions. Do not use other functions and model data for responding to questions"));
options.Messages.Add(new ChatMessage(ChatRole.Assistant, "Hello! How can I help you?"));
PrintBotMessage("Hello! How can I help you?");

#region Function Definitions
var getLeaveFuntionDefinition = new FunctionDefinition()
{
    Name = "get_user_leave_balance",
    Description = "Get the leave balances for the user for a specific leave type",
    Parameters = BinaryData.FromObjectAsJson(
    new
    {
        Type = "object",
        Properties = new
        {
            TypeOfLeave = new
            {
                Type = "string",
                Description = "The type of leave. eg., PaidLeaves, MedicalLeave",
                Enum = new[] { "PaidLeaves", "MedicalLeave", "PaternityLeave", "MaternityLeave" }
            }
        },
        Required = new[] { "TypeOfLeave" },
    },
    new JsonSerializerOptions() {  PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
};
options.Functions.Add(getLeaveFuntionDefinition);

var getDayOfWeekFunction = new FunctionDefinition(){
    Name = "get_day_of_week",
    Description = "Gets the day of week for a specific date",
    Parameters = BinaryData.FromObjectAsJson(new {
        Type = "object",
        Properties = new {
            GivenDate = new {
                Type = "string",
                Description = "A calendar date",
            }
        },
        Required = new[] { "givenDate" },
    }, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
};
options.Functions.Add(getDayOfWeekFunction);
#endregion

var userMessage = GetUserMessage();
options.Messages.Add(userMessage);

do {
    // Get the info from GPT4, and respond back
    Response<ChatCompletions> response = await client.GetChatCompletionsAsync(config.GetValue<string>("deploymentName"), options);
    ChatChoice chatResponse = response.Value.Choices[0];
    options.Messages.Add(chatResponse.Message);

    if(chatResponse.FinishReason == CompletionsFinishReason.FunctionCall) {
        // Make function call -> Get Data -> Pass to chatCompletetions -> respond with completion
        string functionArgs = chatResponse.Message.FunctionCall.Arguments;
        string functionResponse = "";

        if(chatResponse.Message.FunctionCall.Name == "get_day_of_week") {
            functionResponse = getDayOfWeek(functionArgs);
        } else {
            functionResponse = getLeaveBalance(functionArgs);
        }

        options.Messages.Add(
            new() {
                Role = ChatRole.Function,
                Name = chatResponse.Message.FunctionCall.Name,
                Content = functionResponse
            }
        );
        response = await client.GetChatCompletionsAsync(config.GetValue<string>("deploymentName"), options);
        chatResponse = response.Value.Choices[0];
        options.Messages.Add(chatResponse.Message);
    }

    PrintBotMessage(chatResponse.Message.Content);
    userMessage = GetUserMessage();   
    options.Messages.Add(userMessage);

} while(!String.IsNullOrEmpty(userMessage.Content) && !userMessage.Content.Equals("exit", StringComparison.CurrentCultureIgnoreCase));

PrintBotMessage("Thank you!");

void PrintBotMessage(string str) {
    Console.WriteLine($"Agent : {str}");
}

ChatMessage GetUserMessage() {
    Console.Write($"User : ");
    var prompt = Console.ReadLine();
    return new ChatMessage(ChatRole.User, prompt);
}


#region  Functions Code
string getDayOfWeek(string rawdate) {
    DateRequest date = JsonSerializer.Deserialize<DateRequest>(rawdate);
    DateTime dt;
    if(String.Equals(date.givenDate, "today", StringComparison.CurrentCultureIgnoreCase))
    {
        dt = DateTime.Now;
    } else {
        try {
            dt = DateTime.Parse(date.givenDate);
        } catch (Exception ex) {

            return $"The given date {date.givenDate} is not valid. Please provide a date in this format YYYY-MM-DD";
        }
    }
    // Find the day of the week
    string dayOfWeek =dt.ToString("dddd");
    return $"{date.givenDate} is a { dayOfWeek }";
}

string getLeaveBalance(string type) {
    String rawData = new StreamReader("leaveBalances.json").ReadToEnd();;
    return rawData;
}

public class DateRequest {
    public string givenDate {get; set;} = "today";
}
public class LeaveRequest {
    public string typeOfLeave {get; set;} = string.Empty;
}
#endregion