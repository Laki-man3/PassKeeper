using PassKeeper.Core.AutoType;

namespace PassKeeper.Tests;

public class FieldClassifierTests
{
    [Theory]
    [InlineData(true, "anything", FieldKind.Password)]
    [InlineData(false, "Email or phone", FieldKind.Login)]
    [InlineData(false, "Телефон или логин", FieldKind.Login)]
    [InlineData(false, "Электронная почта", FieldKind.Email)]
    [InlineData(false, "Логин", FieldKind.Login)]
    [InlineData(false, "username", FieldKind.Login)]
    [InlineData(false, "Номер телефона", FieldKind.Phone)]
    [InlineData(false, "Enter the 6-digit verification code", FieldKind.Otp)]
    [InlineData(false, "Код подтверждения из SMS", FieldKind.Otp)]
    [InlineData(false, "API key", FieldKind.Key)]
    [InlineData(false, "Лицензионный ключ", FieldKind.Key)]
    public void Classifies(bool isPassword, string name, FieldKind expected) =>
        Assert.Equal(expected, FieldClassifier.Classify(isPassword, name));

    [Theory]
    [InlineData("Search")]
    [InlineData("Поиск по сайту")]
    [InlineData("Address and search bar")]
    [InlineData("Comment")]
    [InlineData("")]
    public void IgnoresOtherFields(string name) => Assert.Null(FieldClassifier.Classify(false, name));
}
