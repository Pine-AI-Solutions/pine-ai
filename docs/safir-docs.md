# سامانه ارسال پیام در بله (سفیر)

سرویس سفیر، سرویس ارسال پیام در بازوهای بله است که به صورت RESTFUL پیاده‌سازی شده
است. با استفاده از این سرویس میتوانید انواع مختلفی از پیامها را برای کاربران موردنظر خود ارسال
کنید. برای استفاده api های این سرویس، لازم است Api Access Key سازمان خود را در هر فراخوانی
API از سرویس ارسال کنید.

## سرویس ارسال پیام

از این سرویس به منظور ارسال پیام به شماره تماس مشخص شده استفاده می شود:

### آدرس سرویس ارسال پیام

```text
Protocol: https

Method: POST

Type: JSON

Url: https://safir.bale.ai/api/v3/send_message
```

---

### ساختار ورودی ارسال پیام

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| api-access-key (Header) | String | بله | برای احراز هویت لازم است این فیلد را مقداردهی کنید. `Key Access Api` سازمان پس از ساخت سفیر در [پنل کسب و کار بله (opens in a new tab)](https://business.bale.ai/) در اختیار شما قرار می‌گیرد. |
| request_id (Body) | String | خیر | این فیلد به منظور تضمین عدم ارسال پیام تکراری در صورت تکرار درخواست استفاده می‌شود. توضیحات |
| bot_id (Body) | Integer | بله | شناسه عددی بازویی که می‌خواهید با آن پیامتان را ارسال کنید. |
| phone_number (Body) | String | بله | شماره تلفن مقصد (باید با 98 شروع شود و بدون کاراکتر اضافه) |
| message_data (Body) | Object of MessageData | بله | اطلاعات پیام مورد نظر برای ارسال |

---

#### ساختار `MessageData`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| message | Object of Message | خیر | اطلاعات پیام |
| otp_message | Object of OTPMessage | خیر | اطلاعات ارسال پیام رمز یکبار مصرف |
| is_secure | Boolean | خیر | برای ارسال پیام رمزدار مقدار این فیلد را برابر با `true` قرار دهید |

---

#### ساختار `Message`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| text | String | خیر | متن پیام ارسالی |
| file_id | String | خیر | آیدی فایلی که از طریق upload_file دریافت کرده‌اید |
| copy_text | String | خیر | با مقداردهی این فیلد، کاربر میتواند با کلیک بر روی دکمه رونوشت مقدار را کپی کند. |
| reply_markup | Object of ReplyMarkup | خیر | صفحه‌کلید inline که به یک پیام پیوست شده است. |

---

#### ساختار `ReplyMarkup`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| inline_keyboard | Array of Array of InlineKeyboardButton | بله | آرایه‌ای از آرایه دکمه‌های شیشه‌ای. هر آرایه داخلی یک ردیف از دکمه‌ها را تشکیل می‌دهد. |

---

#### ساختار `InlineKeyboardButton`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| text | String | بله | متن نمایشی دکمه |
| url | String | خیر | آدرسی که با فشردن دکمه باید باز شود. |
| web_app | Object of WebAppInfo | خیر | اطلاعات مینی‌اپی که با فشردن دکمه باز می‌شود. |
| copy_text | String | خیر | متنی که با فشردن دکمه رونوشت می‌شود. |

> **توجه:** از میان فیلدهای `url`، `web_app` و `copy_text` باید حداقل یکی مقداردهی شده باشد.

---

#### ساختار `WebAppInfo`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| url | String | بله | حاوی اطلاعات لازم در مورد مینی‌اپی است که با فشردن دکمه توسط کاربر باز می‌شود. |

---

#### ساختار `OTPMessage`

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| otp | String | بله | متن رمز یکبار مصرف ارسالی. در حال حاضر تنها رمز های عددی پشتیبانی می‌شوند. |

---

#### ساختار شماره تماس‌های ارسالی

برای استفاده از سرویس ارسال پیام، شماره‌های ارسالی باید با پیش شمارٔه ،۹۸ ۹۸+ شروع شود و شامل ده رقم
بعد از پیش شماره باشد. هرگونه کاراکترهای اضافی مانند خط، فاصله و نیمخط و ... در شماره تلفن باعث دریافت
خطای شماره تماس نامعتبر خواهد شد

✅ نمونه صحیح:

- `989196111003`
- `+98919611003`

❌ نمونه ناصحیح:

- `09196111003`
- `0919-611-1003`
- `9196111003`

---

### ساختار خروجی ارسال پیام

| فیلد | نوع | توضیح |
| --- | --- | --- |
| message_id | String | شناسه پیام (برای استفاده در سرویس‌های دیگر) |
| error_data | Array of ErrorInfo | لیست خطاها |

---

#### ساختار `ErrorInfo`

| فیلد | نوع | توضیح |
| --- | --- | --- |
| phone_number | String | شماره‌ای که خطا برایش رخ داده |
| code | Integer | کد خطا |
| description | String | توضیح خطا |

---

#### مقادیر `Error Code`

| کد | نام | توضیح |
| --- | --- | --- |
| 2 | InternalServerError | خطای داخلی سرور |
| 3 | RateLimitExceeded | بیش از حد مجاز پیام ارسال شده |
| 4 | InvalidInput | ورودی JSON نامعتبر |
| 8 | InvalidPhone | شماره اشتباه |
| 17 | NotBaleUser | کاربر اکانت بله ندارد |
| 20 | PaymentRequired | اعتبار کافی وجود ندارد |
| 21 | MaximumContactLimitReached | به محدودیت تعداد مخاطبین بازو رسیده‌اید |

---

### نحوه استفاده از سرویس ارسال پیام

این api به شما اجازه میدهد که انواع مختلفی از پیامها را تنها با تغییر در مقادیر درخواست، ارسال کنید. در
ادامه نحوه ارسال انواع پیامهای مختلف توضیح داده میشود.

#### ارسال پیام متنی

برای ارسال پیام متنی کافیست Body ریکوئست را به صورت زیر مقداردهی کنید. دقت کنید text را میتوانید به
صورت دلخواه تعیین کنید. به جای >message-text-your >متن مورد نظر خود برای ارسال پیام را قرار دهید

##### نمونه JSON

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"message"
:
 {

      
"text"
:
 
"<your-text-message>"

    }

  }

}
```

##### نمونه Curl

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 
--header
 
'api-access-key: <user-api-access-key>'
 
--header
 
'Content-Type: application/json'
 
--data
 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "message": {

      "text": "test text message"

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"523e6875-7c41-491b-8460-04b33039d7fc"
,

  
"error_data"
:
 
null

}
```

---

#### ارسال پیام چندرسانه‌ای

برای ارسال پیام چندرسانه‌ای از قبیل عکس یا ویدیو، ابتدا باید با استفاده از سرویس upload file،
فایل مورد نظر
خود را آپلود کنید و شناسٔه یکتای فایل خود را دریافت کنید. سپس با مقداردهی آن به صورتی که در زیر توضیح
داده شده است می‌توانید پیام خود را ارسال کنید.

##### نمونه JSON

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"message"
:
 {

      
"text"
:
 
"<your-message-caption>"
,

      
"file_id"
:
 
"<your-file-id>"

    }

  }

}
```

##### نمونه Curl

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 
--header
 
'api-access-key: <user-api-access-key>'
 
--header
 
'Content-Type: application/json'
 
--data
 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "message": {

      "text": "test caption",

      "file_id": "987141dd2672149..."

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"523e6875-7c41-491b-8460-04b33039d7fc"
,

  
"error_data"
:
 
null

}
```

---

#### ارسال پیام رمزدار

پیام رمزدار قابلیتی‌ست که از آن می‌توان برای ارسال پیام‌های محرمانه توسط بازوها استفاده کرد. این نوع
پیام، لایه‌ای از امنیت و حریم خصوصی را برای محتوای ارسالی فراهم می‌کند. برای مشاهدٔه محتوای یک پیام
رمزدار، کاربر باید یک رمز عبور صحیح را وارد کند. این ویژگی برای پیام‌هایی که در آن اطلاعات حساس، محرمانه یا
شخصی ارسال می‌شوند، بسیار مفید است.
برای استفاده از این قابلیت، فارغ از نوع پیامی که قصد ارسال آن را دارید، کافیست فیلد مخصوص آن را فعال
کنید تا پیام شما با این قابلیت ارسال شود.

##### نمونه JSON پیام متنی

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"is_secure"
:
 
true
,

    
"message"
:
 {

      
"text"
:
 
"<your-text-message>"

    }

  }

}
```

##### نمونه JSON پیام چندرسانه‌ای

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"is_secure"
:
 
true
,

    
"message"
:
 {

      
"text"
:
 
"<your-message-caption>"
,

      
"file_id"
:
 
"<your-file-id>"

    }

  }

}
```

##### نمونه Curl پیام متنی

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 
--header
 
'api-access-key: <user-access-token>'
 
--header
 
'Content-Type: application/json'
 
--data
 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "is_secure": true,

    "message": {

      "text": "test text message"

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"523e6875-7c41-491b-8460-04b33039d7fc"
,

  
"error_data"
:
 
null

}
```

---

#### ارسال پیام رمز یکبار مصرف

برای ارسال پیام رمز یکبار مصرف (OTP) نوع پیام متفاوتی تعریف شده است که از طریق مقدار دهی اطلاعات آن، پیام OTP با امکانات
ویژه ارسال می‌شود. با استفاده از این ویژگی، پیام شامل رمز یکبار مصرف در [بازوی رمز یکبار‌مصرف بله (opens in a new tab)](https://ble.ir/verification_code_bot)
ارسال می‌شود. همچنین به صورت خودکار نام بازوی معرفی شده شما و دکمه رونوشت otp به پیام ارسالی اضافه می شود. فرمت پیام ارسالی نهایی به صورت زیر است:

```text
رمز یک‌بارمصرف شما: 123456

⚠️ هشدار!‌ 

لطفا از هویت ارسال کننده کد اطمینان حاصل فرمایید.

از طرف: بازوی رسمی کالابرگ
```

نحوه استفاده از این قابلیت در ادامه توضیح داده می‌شود.

##### نمونه JSON

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,
 
//بازویی که میخواهید نام آن در پیام ارسالی اضافه شود.

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"otp_message"
:
 {

      
"otp"
:
 
"<your-OTP>"

    }

  }

}
```

##### نمونه Curl

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 
--header
 
'api-access-key: <user-api-access-key>'
 
--header
 
'Content-Type: application/json'
 
--data
 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "otp_message": {

      "otp": "123456"

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"BvQjaR.fIKt7kH.EXTddgYduJ2"

}
```

---

#### ارسال پیام با دکمه‌های شیشه‌ای

با استفاده از فیلد `reply_markup` می‌توان یک صفحه‌کلید شیشه‌ای (دکمه‌های تعاملی زیر پیام) به پیام ارسالی پیوست کرد. هر دکمه می‌تواند یکی از سه عملکرد باز کردن لینک (`url`)، باز کردن مینی‌اپ (`web_app`) یا کپی متن (`copy_text`) را داشته باشد.

##### نمونه JSON (دکمه آدرس)

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"message"
:
 {

      
"text"
:
 
"<your-text-message>"
,

      
"reply_markup"
:
 {

        
"inline_keyboard"
:
 [

          [

            { 
"text"
:
 
"مشاهده سایت"
,
 
"url"
:
 
"https://bale.ai"
 }

          ]

        ]

      }

    }

  }

}
```

##### نمونه Curl (دکمه آدرس)

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 
--header
 
'api-access-key: <user-api-access-key>'
 
--header
 
'Content-Type: application/json'
 
--data
 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "message": {

      "text": "برای اطلاعات بیشتر روی دکمه زیر کلیک کنید.",

      "reply_markup": {

        "inline_keyboard": [

          [

            { "text": "مشاهده سایت", "url": "https://bale.ai" }

          ]

        ]

      }

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"523e6875-7c41-491b-8460-04b33039d7fc"
,

  
"error_data"
:
 
null

}
```

---

#### ارسال پیام با قالب پیام

ارسال پیام با قالب پیام، قابلیتی است که به ارسال‌کننده اجازه می‌دهد پیام مورد نظر خود را با مقادیر دلخواه پر کرده و به شماره مقصد ارسال کند.

این ویژگی به سازمان‌ها امکان می‌دهد پیام‌های تبلیغاتی و خدماتی خود را از یکدیگر تفکیک کرده و هر دسته با قیمت‌گذاری متفاوت محاسبه شود.

**توجه:**
قالب پیام باید ابتدا در پنل کسب‌وکار بله ایجاد شده و پس از تأیید تیم پشتیبانی قابل استفاده خواهد بود. برای ارسال پیام، از `template_id` تعریف‌شده برای قالب استفاده کنید.

##### نمونه JSON

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"template_message"
:
 {

      
"template_id"
:
 
"<template-message-id>"
,

      
"text_fields"
:
 {

        
"<field_1>"
:
 
"<value-1>"
,

        
"<field_2>"
:
 
"<value-2>"
,

        
"<field_3>"
:
 
"<value-3>"
,

        
"<field_4>"
:
 
"<value-4>"

      }

    }

  }

}
```

##### نمونه JSON پیام قالبی رمزدار

برای ارسال قالب پیام به‌صورت رمزدار، کافی است فیلد `is_secure` را برابر `true` قرار دهید.

```json
{

  
"request_id"
:
 
"<random-generated-string>"
,

  
"bot_id"
:
 <your-sender-bot-id>
,

  
"phone_number"
:
 
"<destination-phone-number>"
,

  
"message_data"
:
 {

    
"is_secure"
:
 
true
,

    
"template_message"
:
 {

      
"template_id"
:
 
"<template-message-id>"
,

      
"text_fields"
:
 {

        
"<field_1>"
:
 
"<value-1>"
,

        
"<field_2>"
:
 
"<value-2>"
,

        
"<field_3>"
:
 
"<value-3>"
,

        
"<field_4>"
:
 
"<value-4>"

      }

    }

  }

}
```

فرض کنید قالب پیام زیر در پنل کسب‌وکار ایجاد شده است:

```text
سلام آقای/خانم {{name}}

برای حساب شما مقدار {{credit}} ریال، تخفیف در نظر گرفته شده است.
```

##### نمونه Curl

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/send_message'
 \

--header 
'api-access-key: <user-access-token>'
 \

--header 
'Content-Type: application/json'
 \

--data 
'{

  "request_id": "absdfgesjgo",

  "bot_id": 123456789,

  "phone_number": "989123456789",

  "message_data": {

    "is_secure": true,

    "template_message": {

      "template_id": "asdfg",

      "text_fields": {

        "name": "علی حسینی",

        "credit": "۵۰۰۰۰"

      }

    }

  }

}'
```

✅ پاسخ:

```json
{

  
"message_id"
:
 
"523e6875-7c41-491b-8460-04b33039d7fc"
,

  
"error_data"
:
 
null

}
```

**نکته:** کلیدهای موجود در `text_fields` باید **دقیقاً** مطابق نام فیلدهای تعریف‌شده در قالب پیام در پنل کسب‌وکار باشند. در غیر این صورت درخواست با خطا مواجه خواهد شد.

---

## سرویس بارگذاری فایل

برای مشخص کردن یک فایل در apiهای مختلف لازم است که ابتدا این فایل بارگذاری شود و سپس از آیدی فایل
آپلود شده، در دیگر apiها استفاده شود. به این منظور api زیر مورد استفاده قرار می‌گیرد

### آدرس سرویس

```text
Protocol: https

Method: POST

Type: FormData

Url: https://safir.bale.ai/api/v3/upload_file
```

### ساختار ورودی

| فیلد | نوع | اجباری | توضیح |
| --- | --- | --- | --- |
| api-access-key (Header) | String | بله | برای احراز هویت لازم است این فیلد را مقداردهی کنید. `Key Access Api` سازمان پس از ساخت سفیر در [پنل کسب و کار بله (opens in a new tab)](https://business.bale.ai/) در اختیار شما قرار می‌گیرد. |
| file (Body) | Multipart file | بله | فایل آپلودی (حداکثر 500MB) |

---

### ساختار خروجی

| فیلد | نوع | توضیح |
| --- | --- | --- |
| file_id | String | شناسه یکتای فایل آپلود شده |
| error | ErrorInfo | اطلاعات خطا |

---

### نمونه Curl

```bash
curl
 
--location
 
'https://safir.bale.ai/api/v3/upload_file'
 
/

 
--header
 
'api-access-key:<user-access-token>'
 
/

 
--form
 
'file=@"<file-path>"'
```

✅ پاسخ:

```json
{

  
"file_id"
:
 
"987141dd2672149..."

}
```

---

## مدیریت انواع خطا در سرویس ارسال پیام

- تمام سرویس‌ها دارای فیلد `error_data` هستند.
- ساختار آن برابر با `ErrorInfo` تعریف‌شده در بخش 1.3.1 است.
- در برخی سرویس‌ها ممکن است فقط یک خطا برگردد، ولی در سرویس ارسال پیام برای هر شماره می‌تواند خطای جداگانه وجود داشته
باشد.

---

## مدیریت اثر یکسان ارسال پیام (Idempotency)

- برای جلوگیری از ارسال تکراری پیام از فیلد `request_id` استفاده کنید.
- در صورتی که همان `request_id` دوباره ارسال شود → پیام تکراری ارسال نمی‌شود.
- این فیلد اختیاری است، ولی توصیه می‌شود همیشه استفاده شود.

---

مستندات مینی‌اپ

دوره‌های آموزشی
