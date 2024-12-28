namespace Kp.Ms.Sms.Entities.Enums;

public enum SmsRuErrorCode
{
    ServiceUnavailable = 220, // Сервис временно недоступен, попробуйте чуть позже
    InternalServerError = 500 // Ошибка на сервере. Повторите запрос
}
