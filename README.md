# EntropyTunnel

Зворотний тунель для самостійного розгортання із вбудованим рушієм інженерії хаосу. Відкриває локальний сервіс в інтернет та перехоплює кожен запит на шляху — додає затримки, підміняє відповіді або відтворює збої без жодних змін у коді застосунку.

## Як це працює

Сервер запускається на публічно доступному хості. Агент запускається на вашій машині, підключається до сервера через WebSocket і підтримує з'єднання активним. Коли надходить запит на `yourapp.entropy-tunnel.xyz`, сервер знаходить відповідне WebSocket-з'єднання і пересилає запит через нього. Агент пропускає запит через конвеєр обробки, а потім надсилає відповідь назад.

```
Інтернет → nginx → EntropyTunnel.Server → WebSocket → EntropyTunnel.Client → localhost:3000
                                                           │
                                                    MockEngine
                                                    ChaosEngine
                                                    RequestRouter
                                                    LocalForwarder
```

Панель керування — це окремий застосунок (SPA), який звертається до REST API сервера. Агент працює у фоновому режимі, без власного HTTP-сервера.

## Структура проєкту

```
EntropyTunnel.Server/     - WebSocket-ретранслятор + REST API + SSE-потік логів
  Handlers/               - TunnelHandler (WebSocket), HttpProxyHandler (proxy-обробник)
  Endpoints/              - усі маршрути /api/*
  State/                  - AgentStateStore (правила, логи, ізоляція на рівні агента)
  TunnelHub.cs            - спільний стан і допоміжні методи надсилання

EntropyTunnel.Client/     - фоновий агент, без HTTP-сервера
  Stages/                 - MockEngine, ChaosEngine, RequestRouter, LocalForwarder
  Pipeline/               - IPipelineStage, TunnelContext
  Services/               - TunnelService (BackgroundService, автоматичне перепідключення)
  Multiplexer/            - бінарне кадрування поверх WebSocket

EntropyTunnel.Core/       - спільні моделі та протокол передачі даних
EntropyTunnel.Dashboard/  - SPA на React + TypeScript (окремий проєкт, VITE_API_URL)
```

## Локальний запуск

Запуск сервера:

```bash
dotnet run --project EntropyTunnel.Server
# слухає на порту :8080
```

Налаштування агента у `EntropyTunnel.Client/appsettings.json`:

```json
{
  "TunnelSettings": {
    "ServerUrl": "ws://localhost:8080/tunnel",
    "PublicDomain": "localhost:8080",
    "ClientId": "myapp",
    "LocalPort": 3000
  }
}
```

Запуск агента:

```bash
dotnet run --project EntropyTunnel.Client
# або передати порт та ідентифікатор напряму:
dotnet run --project EntropyTunnel.Client -- 3000 myapp
```

Щоб захистити тунель паролем від публічного доступу:

```bash
dotnet run --project EntropyTunnel.Client -- --password secret 3000 myapp
```

Режим розробки панелі керування (Vite HMR):

```bash
cd EntropyTunnel.Dashboard
npm install && npm run dev
# задати VITE_API_URL=http://localhost:8080 у файлі .env
```

## Протокол передачі даних

Увесь обмін даними між сервером і агентом відбувається через єдине постійне WebSocket-з'єднання. Кожен кадр має 17-байтовий заголовок: 16 байт ідентифікатора запиту (Guid) + 1 байт типу кадру. `Guid.Empty` як ідентифікатор означає керівний кадр, не прив'язаний до жодного HTTP-запиту.

| Напрямок        | Тип                    | Байт   |
| --------------- | ---------------------- | ------ |
| Server → Client | Заголовок запиту       | `0x10` |
| Server → Client | Фрагмент тіла запиту   | `0x11` |
| Server → Client | Кінець запиту          | `0x12` |
| Client → Server | Заголовок відповіді    | `0x01` |
| Client → Server | Фрагмент тіла відповіді| `0x02` |
| Client → Server | Кінець відповіді       | `0x03` |
| Client → Server | Ping                   | `0x00` |
| Server → Client | SyncRules              | `0x20` |
| Client → Server | LogEvent               | `0x21` |
| Server → Client | SessionAuth            | `0x22` |

Правила зберігаються на сервері. Коли агент підключається, сервер надсилає повний набір правил через кадр `0x20 SyncRules`. Після кожної зміни правил через API сервер надсилає їх знову. Агент не втрачає свої правила при перепідключенні.

## Правила хаосу

Правила хаосу зіставляються за HTTP-шляхом і методом. Для запитів, що підпадають під правило, можна:

- **Додати затримку** — фіксовану або з налаштовуваним розкидом
- **Повернути помилку** — задати HTTP-статус із певною ймовірністю (випадковий або пакетний режим)
- **Підмінити відповідь** — повернути статичне тіло і статус, обходячи локальний сервіс

Правила керуються через інтерфейс дашборду або безпосередньо через REST API (`/api/{clientId}/rules/chaos`, `/api/{clientId}/rules/mocks`, `/api/{clientId}/rules/routing`).

## Збірка релізів

Самодостатній виконуваний файл (усе в одному артефакті):

```bash
# Windows
dotnet publish EntropyTunnel.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/client

# Linux
dotnet publish EntropyTunnel.Client -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish/client

# Server
dotnet publish EntropyTunnel.Server -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish/server
```

CI публікує бінарні файли для `win-x64`, `linux-x64` та `osx-arm64` при кожному тезі `v*`.
