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
    Reads passport image and extracts data as JSON.
    Intelligently handles gender abbreviations and century date issues.
    """
    if not os.path.exists(image_path):
        # Return error in JSON format
        return json.dumps({"error": f"File not found at path: {image_path}"}, ensure_ascii=False)

    try:
        # --- 1. Load image and crop MRZ region ---
        image = Image.open(image_path).convert('RGB')
        img_array = np.array(image)
        height, width = img_array.shape[:2]
        
        mrz_height = int(height * 0.35)
        cropped_array = img_array[(height - mrz_height):height, :]

        # --- 2. Enhance image (OpenCV processing) ---
        if len(cropped_array.shape) == 3:
            gray = cv2.cvtColor(cropped_array, cv2.COLOR_RGB2GRAY)
        else:
            gray = cropped_array
            
        denoised = cv2.fastNlMeansDenoising(gray)
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        enhanced = clahe.apply(denoised)
        _, binary = cv2.threshold(enhanced, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        
        mrz_enhanced = Image.fromarray(binary)

        # --- 3. Extract data ---
        img_buffer = io.BytesIO()
        mrz_enhanced.save(img_buffer, format='PNG')
        img_buffer.seek(0)
        
        mrz = read_mrz(img_buffer)
        
        if mrz is None:
            return json.dumps({"error": "Failed to read MRZ region. Ensure the bottom two lines are clear."}, ensure_ascii=False)

        mrz_data = mrz.to_dict()

        # --- 4. Process and format abbreviations (Century problem) ---
        def format_mrz_date(date_str, is_dob=False):
            if not date_str or not date_str.isdigit() or len(date_str) != 6:
                return date_str
            try:
                # Try to convert text to date
                dt = datetime.strptime(date_str, "%y%m%d")
                current_year = datetime.now().year
                
                if is_dob:
                    # Date of birth logic: cannot be in the future
                    # If the calculated year is greater than the current year, subtract 100 years
                    if dt.year > current_year:
                        dt = dt.replace(year=dt.year - 100)
                else:
                    # Expiry date logic: cannot be in the distant past (e.g., more than 20 years ago)
                    # If calculated as a distant past year, add 100 years to make it in the future
                    if dt.year < (current_year - 20):
                        dt = dt.replace(year=dt.year + 100)
                        
                return dt.strftime("%Y-%m-%d")
            except ValueError:
                return date_str

        # Apply formatting function to dates with date type specification
        if 'date_of_birth' in mrz_data:
            mrz_data['date_of_birth_formatted'] = format_mrz_date(mrz_data['date_of_birth'], is_dob=True)
            
        if 'expiration_date' in mrz_data:
            mrz_data['expiration_date_formatted'] = format_mrz_date(mrz_data['expiration_date'], is_dob=False)

        # Format gender
        if 'sex' in mrz_data:
            sex_mapping = {'M': 'Male', 'F': 'Female', '<': 'Unspecified'}
            mrz_data['sex_formatted'] = sex_mapping.get(mrz_data['sex'].upper(), mrz_data['sex'])

        # --- 5. Return result as JSON ---
        # indent=4 to make JSON human-readable and organized
        return json.dumps(mrz_data, ensure_ascii=False, indent=4)

    except Exception as e:
        return json.dumps({"error": f"An error occurred during processing: {str(e)}"}, ensure_ascii=False)
if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "No image path provided"}))
        sys.exit(1)
    
    image_path = sys.argv[1]
    result = process_and_extract_passport_data(image_path)
    print(result)