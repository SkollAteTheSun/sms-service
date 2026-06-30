## Сборка

Скопировать и настроить файл конфигурации:
```bash
cp appsettings.json appsettings.Staging.json
nano appsettings.Staging.json
```

Для сборки выполнить команду:
```bash
dotnet publish -r linux-x64 -c Release --property:PublishDir=./publish /p:EnvironmentName=Staging --self-contained
```

## Сервис

Скопировать в директорию с сервисом:
```bash
rm -rf /root/ms/sms && mv ./publish /root/ms/sms
```

```bash
cp ms-sms.Staging.service /etc/systemd/system/ms-sms.service
```

## Запуск
```bash
sudo systemctl enable ms-sms.service
sudo systemctl start ms-sms.service
sudo systemctl status ms-sms.service
sudo systemctl disable ms-sms.service
sudo systemctl stop ms-sms.service

systemctl daemon-reload
```

## Среды
* Development
* Staging
* Production

Меняем среду на нужную в командах там где она указывается
