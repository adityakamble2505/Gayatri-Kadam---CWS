namespace GreenCarWash.PaymentService.Services;

public static class CardNumbers
{
    public static string Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    public static bool IsValid(string? value)
    {
        var digits = Digits(value);
        return digits.Length is >= 13 and <= 19 && Luhn(digits);
    }

    public static bool Luhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var number = digits[index] - '0';
            if (number is < 0 or > 9)
                return false;
            if (alternate)
            {
                number *= 2;
                if (number > 9)
                    number -= 9;
            }

            sum += number;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static bool Expired(int month, int year, DateTime utcNow)
    {
        if (month is < 1 or > 12 || year < 2000)
            return true;
        if (year > utcNow.Year)
            return false;
        if (year < utcNow.Year)
            return true;
        return month < utcNow.Month;
    }

    public static string Brand(string digits) => digits[0] switch
    {
        '4' => "Visa",
        '5' => "Mastercard",
        '3' => "Amex",
        '6' => "Discover",
        _ => "Card"
    };
}
