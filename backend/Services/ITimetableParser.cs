using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface ITimetableParser
{
    TimetableParseResult Parse(string input, ParseMode mode);
}
