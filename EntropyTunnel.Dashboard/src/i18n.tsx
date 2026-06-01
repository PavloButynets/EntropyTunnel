import { createContext, useContext, useState, type ReactNode } from "react";

export type Lang = "en" | "uk";

const STORAGE_KEY = "et_lang";

function readLang(): Lang {
  const v = localStorage.getItem(STORAGE_KEY);
  return v === "uk" ? "uk" : "en";
}

export type T = {
  // Tabs
  tabChaos: string;
  tabMocks: string;
  tabRouting: string;
  tabLog: string;

  // StatusBar
  statusInspector: string;
  statusConnected: string;
  statusDisconnected: string;
  statusAgent: string;

  // Common
  cancel: string;
  saving: string;
  saveChanges: string;
  enabled: string;
  disabled: string;
  edit: string;
  delete: string;
  name: string;
  pattern: string;
  method: string;
  priority: string;
  any: string;

  // ChaosRules
  chaosDesc: string;
  chaosAdd: string;
  chaosEmpty: string;
  chaosLatency: string;
  chaosErrorRate: string;
  chaosEditTitle: string;
  chaosNewTitle: string;
  chaosRuleName: string;
  chaosHttpMethod: string;
  chaosPathPattern: string;
  chaosLatencyMs: string;
  chaosJitterMs: string;
  chaosErrorRateField: string;
  chaosErrorCode: string;
  chaosErrorBody: string;
  chaosAddBtn: string;
  chaosValidName: string;
  chaosValidPath: string;
  chaosValidGte0: string;
  chaosValidErrorRate: string;
  chaosValidStatus: string;
  chaosDeleteConfirm: (name: string) => string;

  // MockRules
  mockDesc: string;
  mockAdd: string;
  mockEmpty: string;
  mockContentType: string;
  mockEditTitle: string;
  mockNewTitle: string;
  mockStatusCode: string;
  mockContentTypeField: string;
  mockResponseBody: string;
  mockAddBtn: string;
  mockValidStatus: string;
  mockValidContentType: string;
  mockValidName: string;
  mockValidPath: string;
  mockDeleteConfirm: (name: string) => string;

  // RoutingRules
  routingDesc: string;
  routingAdd: string;
  routingEmpty: string;
  routingTarget: string;
  routingEditTitle: string;
  routingNewTitle: string;
  routingPriorityField: string;
  routingTargetUrl: string;
  routingAddBtn: string;
  routingValidTarget: string;
  routingValidTargetFormat: string;
  routingValidPriority: string;
  routingValidName: string;
  routingValidPath: string;
  routingDeleteConfirm: (name: string) => string;

  // RequestLog
  logDesc: string;
  logClear: string;
  logEmpty: string;
  logTime: string;
  logStatus: string;
  logDuration: string;
  logAnnotations: string;
  logPath: string;
  logReqHeaders: string;
  logRespHeaders: string;
  logNoHeaders: string;
  logReqBody: string;
  logFirst2KB: (total: number) => string;
  logNBytes: (n: number) => string;
  logReplayTitle: string;
  logOriginal: string;
  logAddHeader: string;
  logNoHeadersRow: string;
  logHideRespHeaders: (n: number) => string;
  logShowRespHeaders: (n: number) => string;
  logClose: string;
  logSending: string;
  logSendRequest: string;
  logEmptyBody: string;
  logRespBody: string;
  logReplayBtn: string;
  logReplayTitleAttr: string;
  logChaosApplied: string;
  logMockApplied: string;
  logRequestFailed: string;

  // LoginForm
  loginSub: string;
  loginPlaceholder: string;
  loginError: string;
  loginSigningIn: string;
  loginSignIn: string;

  // AccountOverview
  overviewSubtitle: string;
  overviewNoAgents: string;
  overviewHint: string;
  overviewHintEnd: string;
  overviewJustNow: string;
  overviewMinAgo: (m: number) => string;
  overviewHourAgo: (h: number) => string;
  overviewConnected: string;
};

const en: T = {
  tabChaos: "Failures & Delays",
  tabMocks: "Mock Responses",
  tabRouting: "Routing",
  tabLog: "Request Log",

  statusInspector: "Request Log",
  statusConnected: "Connected",
  statusDisconnected: "Disconnected",
  statusAgent: "Agent:",

  cancel: "Cancel",
  saving: "Saving…",
  saveChanges: "Save Changes",
  enabled: "Enabled",
  disabled: "Disabled",
  edit: "Edit",
  delete: "Delete",
  name: "Name",
  pattern: "Pattern",
  method: "Method",
  priority: "Priority",
  any: "Any",

  chaosDesc: "Add delays or synthetic errors to matching requests.",
  chaosAdd: "+ Add Rule",
  chaosEmpty: "No failure rules yet.",
  chaosLatency: "Latency",
  chaosErrorRate: "Error Probability",
  chaosEditTitle: "Edit Failure Rule",
  chaosNewTitle: "New Failure Rule",
  chaosRuleName: "Rule Name *",
  chaosHttpMethod: "HTTP Method",
  chaosPathPattern: "Path Pattern *",
  chaosLatencyMs: "Latency (ms)",
  chaosJitterMs: "Spread ±(ms)",
  chaosErrorRateField: "Error Probability (0–100 %)",
  chaosErrorCode: "Error Status Code",
  chaosErrorBody: "Error Response Body",
  chaosAddBtn: "Add Rule",
  chaosValidName: "Name is required",
  chaosValidPath: "Path pattern is required",
  chaosValidGte0: "Must be ≥ 0",
  chaosValidErrorRate: "Must be 0–100 %",
  chaosValidStatus: "Must be a valid HTTP status (100–599)",
  chaosDeleteConfirm: (name) => `Delete failure rule "${name}"?`,

  mockDesc: "Return predefined responses — useful when the backend is not ready.",
  mockAdd: "+ Add Response",
  mockEmpty: "No mock responses yet.",
  mockContentType: "Content-Type",
  mockEditTitle: "Edit Mock Response",
  mockNewTitle: "New Mock Response",
  mockStatusCode: "Status Code *",
  mockContentTypeField: "Content-Type *",
  mockResponseBody: "Response Body",
  mockAddBtn: "Add Response",
  mockValidStatus: "Must be a valid HTTP status (100–599)",
  mockValidContentType: "Content-Type is required",
  mockValidName: "Name is required",
  mockValidPath: "Path pattern is required",
  mockDeleteConfirm: (name) => `Delete mock response "${name}"?`,

  routingDesc:
    "Route requests with different paths to different local services (e.g., /api/* → :5000, rest → :5173).",
  routingAdd: "+ Add Rule",
  routingEmpty:
    "No routing rules — all traffic goes to the default local port.",
  routingTarget: "Target Service",
  routingEditTitle: "Edit Routing Rule",
  routingNewTitle: "New Routing Rule",
  routingPriorityField: "Priority (lower number = higher priority)",
  routingTargetUrl: "Service Base URL *",
  routingAddBtn: "Add Rule",
  routingValidTarget: "Service base URL is required",
  routingValidTargetFormat: "Must start with http:// or https://",
  routingValidPriority: "Priority must be ≥ 0",
  routingValidName: "Name is required",
  routingValidPath: "Path pattern is required",
  routingDeleteConfirm: (name) => `Delete routing rule "${name}"?`,

  logDesc:
    "Log of the last 1,000 requests. Click a row to see headers and body.",
  logClear: "Clear Log",
  logEmpty: "No requests yet — send traffic through the tunnel.",
  logTime: "Time",
  logStatus: "Status",
  logDuration: "Duration",
  logAnnotations: "Annotations",
  logPath: "Path",
  logReqHeaders: "Request Headers",
  logRespHeaders: "Response Headers",
  logNoHeaders: "Headers not captured",
  logReqBody: "Request Body",
  logFirst2KB: (total) => `(first 2 KB of ${total} bytes)`,
  logNBytes: (n) => `(${n} bytes)`,
  logReplayTitle: "Replay Request",
  logOriginal: "Original request:",
  logAddHeader: "+ Add",
  logNoHeadersRow: "No headers",
  logHideRespHeaders: (n) => `Hide Response Headers (${n})`,
  logShowRespHeaders: (n) => `Show Response Headers (${n})`,
  logClose: "Close",
  logSending: "⏳ Sending…",
  logSendRequest: "↩ Send Request",
  logEmptyBody: "(empty)",
  logRespBody: "Response Body",
  logReplayBtn: "↩ Replay",
  logReplayTitleAttr: "Replay request with edits",
  logChaosApplied: "Failure rule applied",
  logMockApplied: "Mock response returned",
  logRequestFailed: "Request failed",

  loginSub: "Enter your account password",
  loginPlaceholder: "Password",
  loginError: "Incorrect password. Check the agent output.",
  loginSigningIn: "Signing in…",
  loginSignIn: "Sign in",

  overviewSubtitle: "Your active tunnels",
  overviewNoAgents: "No agents connected.",
  overviewHint: "Start an agent with",
  overviewHintEnd: "and it will appear here.",
  overviewJustNow: "just now",
  overviewMinAgo: (m) => `${m}m ago`,
  overviewHourAgo: (h) => `${h}h ago`,
  overviewConnected: "Connected",
};

const uk: T = {
  tabChaos: "Збої та затримки",
  tabMocks: "Імітації відповідей",
  tabRouting: "Маршрутизація",
  tabLog: "Журнал запитів",

  statusInspector: "Журнал запитів",
  statusConnected: "Підключено",
  statusDisconnected: "Не підключено",
  statusAgent: "Агент:",

  cancel: "Скасувати",
  saving: "Збереження…",
  saveChanges: "Зберегти зміни",
  enabled: "Увімкнено",
  disabled: "Вимкнено",
  edit: "Редагувати",
  delete: "Видалити",
  name: "Назва",
  pattern: "Шаблон",
  method: "Метод",
  priority: "Пріоритет",
  any: "Будь-який",

  chaosDesc:
    "Додає затримки або штучні помилки до запитів, що відповідають правилу.",
  chaosAdd: "+ Додати правило",
  chaosEmpty: "Правил збоїв ще немає.",
  chaosLatency: "Затримка",
  chaosErrorRate: "Імовірність помилки",
  chaosEditTitle: "Редагувати правило збоїв",
  chaosNewTitle: "Нове правило збоїв",
  chaosRuleName: "Назва правила *",
  chaosHttpMethod: "HTTP-метод",
  chaosPathPattern: "Шаблон шляху *",
  chaosLatencyMs: "Затримка (мс)",
  chaosJitterMs: "Розкид ±(мс)",
  chaosErrorRateField: "Імовірність помилки (0–100 %)",
  chaosErrorCode: "HTTP-статус помилки",
  chaosErrorBody: "Тіло помилкової відповіді",
  chaosAddBtn: "Додати правило",
  chaosValidName: "Назва обов'язкова",
  chaosValidPath: "Шаблон шляху обов'язковий",
  chaosValidGte0: "Має бути ≥ 0",
  chaosValidErrorRate: "Має бути 0–100 %",
  chaosValidStatus: "Має бути дійсним HTTP-статусом (100–599)",
  chaosDeleteConfirm: (name) => `Видалити правило збоїв "${name}"?`,

  mockDesc:
    "Повертає підготовлені відповіді — зручно, коли серверна частина ще не готова.",
  mockAdd: "+ Додати відповідь",
  mockEmpty: "Імітаційних відповідей ще немає.",
  mockContentType: "Content-Type",
  mockEditTitle: "Редагувати імітаційну відповідь",
  mockNewTitle: "Нова імітаційна відповідь",
  mockStatusCode: "HTTP-статус *",
  mockContentTypeField: "Content-Type *",
  mockResponseBody: "Тіло відповіді",
  mockAddBtn: "Додати відповідь",
  mockValidStatus: "Має бути дійсним HTTP-статусом (100–599)",
  mockValidContentType: "Content-Type обов'язковий",
  mockValidName: "Назва обов'язкова",
  mockValidPath: "Шаблон шляху обов'язковий",
  mockDeleteConfirm: (name) => `Видалити імітаційну відповідь "${name}"?`,

  routingDesc:
    "Спрямовує запити з різними шляхами до різних локальних сервісів (наприклад, /api/* → :5000, решта → :5173).",
  routingAdd: "+ Додати правило",
  routingEmpty:
    "Правил маршрутизації немає — усі запити йдуть на локальний порт за замовчуванням.",
  routingTarget: "Цільовий сервіс",
  routingEditTitle: "Редагувати правило маршрутизації",
  routingNewTitle: "Нове правило маршрутизації",
  routingPriorityField: "Пріоритет (менше число — вище правило)",
  routingTargetUrl: "Базова URL-адреса сервісу *",
  routingAddBtn: "Додати правило",
  routingValidTarget: "Базова URL-адреса сервісу обов'язкова",
  routingValidTargetFormat: "Має починатися з http:// або https://",
  routingValidPriority: "Пріоритет має бути ≥ 0",
  routingValidName: "Назва обов'язкова",
  routingValidPath: "Шаблон шляху обов'язковий",
  routingDeleteConfirm: (name) => `Видалити правило маршрутизації "${name}"?`,

  logDesc:
    "Журнал останніх 1000 запитів. Натисніть на рядок, щоб переглянути заголовки й тіло запиту.",
  logClear: "Очистити журнал",
  logEmpty: "Запитів ще немає — надішліть запит через тунель.",
  logTime: "Час",
  logStatus: "Статус",
  logDuration: "Тривалість",
  logAnnotations: "Позначки",
  logPath: "Шлях",
  logReqHeaders: "Заголовки запиту",
  logRespHeaders: "Заголовки відповіді",
  logNoHeaders: "Заголовки не збережено",
  logReqBody: "Тіло запиту",
  logFirst2KB: (total) => `(перші 2 КБ з ${total} байт)`,
  logNBytes: (n) => `(${n} байт)`,
  logReplayTitle: "Повтор запиту",
  logOriginal: "Початковий запит:",
  logAddHeader: "+ Додати",
  logNoHeadersRow: "Немає заголовків",
  logHideRespHeaders: (n) => `Сховати заголовки відповіді (${n})`,
  logShowRespHeaders: (n) => `Показати заголовки відповіді (${n})`,
  logClose: "Закрити",
  logSending: "⏳ Надсилання…",
  logSendRequest: "↩ Надіслати запит",
  logEmptyBody: "(порожньо)",
  logRespBody: "Тіло відповіді",
  logReplayBtn: "↩ Повторити",
  logReplayTitleAttr: "Повторити запит зі змінами",
  logChaosApplied: "Застосовано правило збоїв",
  logMockApplied: "Повернено імітаційну відповідь",
  logRequestFailed: "Запит не виконано",

  loginSub: "Введіть пароль облікового запису",
  loginPlaceholder: "Пароль",
  loginError: "Неправильний пароль. Перевірте повідомлення агента.",
  loginSigningIn: "Вхід…",
  loginSignIn: "Увійти",

  overviewSubtitle: "Ваші активні тунелі",
  overviewNoAgents: "Агентів немає.",
  overviewHint: "Запустіть агента командою",
  overviewHintEnd: "і він з'явиться тут.",
  overviewJustNow: "щойно",
  overviewMinAgo: (m) => `${m} хв тому`,
  overviewHourAgo: (h) => `${h} год тому`,
  overviewConnected: "Підключено",
};

const dict: Record<Lang, T> = { en, uk };

interface LangContextValue {
  lang: Lang;
  t: T;
  toggle: () => void;
}

export const LangContext = createContext<LangContextValue>({
  lang: "en",
  t: en,
  toggle: () => {},
});

export function LangProvider({ children }: { children: ReactNode }) {
  const [lang, setLang] = useState<Lang>(readLang);

  function toggle() {
    const next: Lang = lang === "en" ? "uk" : "en";
    localStorage.setItem(STORAGE_KEY, next);
    setLang(next);
  }

  return (
    <LangContext.Provider value={{ lang, t: dict[lang], toggle }}>
      {children}
    </LangContext.Provider>
  );
}

export function useT() {
  return useContext(LangContext);
}
