import sys
import os
import io
import cv2
import json
import numpy as np
from PIL import Image
from passporteye import read_mrz
from datetime import datetime
import pytesseract
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')
pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

def process_and_extract_passport_data(image_path):
    """
    تقرأ صورة الجواز وتستخرج البيانات كـ JSON.
    تعالج اختصارات الجنس ومشكلة القرن للتواريخ بشكل ذكي.
    """
    if not os.path.exists(image_path):
        # إرجاع الخطأ بصيغة JSON
        return json.dumps({"error": f"الملف غير موجود في المسار: {image_path}"}, ensure_ascii=False)

    try:
        # --- 1. تحميل الصورة وقص منطقة الـ MRZ ---
        image = Image.open(image_path).convert('RGB')
        img_array = np.array(image)
        height, width = img_array.shape[:2]
        
        mrz_height = int(height * 0.35)
        cropped_array = img_array[(height - mrz_height):height, :]

        # --- 2. تحسين الصورة (معالجة OpenCV) ---
        if len(cropped_array.shape) == 3:
            gray = cv2.cvtColor(cropped_array, cv2.COLOR_RGB2GRAY)
        else:
            gray = cropped_array
            
        denoised = cv2.fastNlMeansDenoising(gray)
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        enhanced = clahe.apply(denoised)
        _, binary = cv2.threshold(enhanced, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        
        mrz_enhanced = Image.fromarray(binary)

        # --- 3. استخراج البيانات ---
        img_buffer = io.BytesIO()
        mrz_enhanced.save(img_buffer, format='PNG')
        img_buffer.seek(0)
        
        mrz = read_mrz(img_buffer)
        
        if mrz is None:
            return json.dumps({"error": "فشل في قراءة منطقة MRZ. تأكد من وضوح السطرين السفليين."}, ensure_ascii=False)

        mrz_data = mrz.to_dict()

        # --- 4. معالجة وتنسيق الاختصارات (مشكلة القرن) ---
        def format_mrz_date(date_str, is_dob=False):
            if not date_str or not date_str.isdigit() or len(date_str) != 6:
                return date_str
            try:
                # محاولة تحويل النص إلى تاريخ
                dt = datetime.strptime(date_str, "%y%m%d")
                current_year = datetime.now().year
                
                if is_dob:
                    # منطق تاريخ الميلاد: لا يمكن أن يكون في المستقبل
                    # إذا كانت السنة المحسوبة أكبر من السنة الحالية، نطرح 100 عام
                    if dt.year > current_year:
                        dt = dt.replace(year=dt.year - 100)
                else:
                    # منطق تاريخ الانتهاء: لا يمكن أن يكون في ماضي بعيد جداً (مثلا قبل 20 سنة من الآن)
                    # إذا تم حسابها كسنة ماضية بعيدة، نضيف 100 عام لتصبح في المستقبل
                    if dt.year < (current_year - 20):
                        dt = dt.replace(year=dt.year + 100)
                        
                return dt.strftime("%Y-%m-%d")
            except ValueError:
                return date_str

        # تطبيق دالة التنسيق على التواريخ مع تحديد نوع التاريخ
        if 'date_of_birth' in mrz_data:
            mrz_data['date_of_birth_formatted'] = format_mrz_date(mrz_data['date_of_birth'], is_dob=True)
            
        if 'expiration_date' in mrz_data:
            mrz_data['expiration_date_formatted'] = format_mrz_date(mrz_data['expiration_date'], is_dob=False)

        # تنسيق الجنس
        if 'sex' in mrz_data:
            sex_mapping = {'M': 'Male', 'F': 'Female', '<': 'Unspecified'}
            mrz_data['sex_formatted'] = sex_mapping.get(mrz_data['sex'].upper(), mrz_data['sex'])

        # --- 5. إرجاع النتيجة كـ JSON ---
        # indent=4 لجعل شكل الـ JSON مقروء ومرتب
        return json.dumps(mrz_data, ensure_ascii=False, indent=4)

    except Exception as e:
        return json.dumps({"error": f"حدث خطأ أثناء المعالجة: {str(e)}"}, ensure_ascii=False)
if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "No image path provided"}))
        sys.exit(1)
    
    image_path = sys.argv[1]
    result = process_and_extract_passport_data(image_path)
    print(result)