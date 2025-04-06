.code
PUBLIC _DllMainCRTStartup

; _DllMainCRTStartup - Punkt wej�cia dla biblioteki DLL.
; Zwraca TRUE (sukces), aby wskaza� pomy�ln� inicjalizacj�.
_DllMainCRTStartup PROC
    mov eax, 1  ; Zwracamy TRUE (sukces)
    ret
_DllMainCRTStartup ENDP
PUBLIC ApplyGaussianBlurAsm
ApplyGaussianBlurAsm PROC
    push rbp                            ; Zapisanie bazowego wska�nika stosu
    mov rbp, rsp                        ; Ustawienie nowej ramki stosu
    sub rsp, 80                         ; Rezeracja 80 bajt�w na zmienne lokalne
    push rbx                            ; Zapisanie rejestr�w
    push rsi 
    push rdi
    push r12
    push r13
    push r14
    push r15
    
    ; Mapowanie parametr�w na stos:
    ; [rbp-8]  = mask_ptr
    ; [rbp-16] = pixelData
    ; [rbp-24] = resultData
    ; [rbp-28] = stride
    ; [rbp-32] = maskSize
    ; [rbp-36] = radius
    ; [rbp-40] = xPos
    ; [rbp-44] = yPos
    ; [rbp-48] = imgWidth
    
    mov [rbp-16], rcx         ; Zapisanie wska�nik do orygina�u
    mov [rbp-24], rdx         ; Zapisanie wska�nik do wyniku
    mov [rbp-40], r8d         ; X 
    mov [rbp-44], r9d         ; Y
    mov eax, dword ptr [rbp+48]
    mov [rbp-28], eax        ; Stride - szeroko�� linii
    mov rax, [rbp+56]
    mov [rbp-8], rax         ; Gaussian mask
    mov eax, dword ptr [rbp+64]
    mov [rbp-32], eax        ; Mask size
    mov eax, dword ptr [rbp+72]
    mov [rbp-36], eax        ; Blur radius
    mov eax, dword ptr [rbp+80]
    mov [rbp-48], eax        ; Image width
    
    ; Inicjalizacja sum
    xorpd xmm0, xmm0         ; Blue accumulator
    xorpd xmm1, xmm1         ; Green accumulator
    xorpd xmm2, xmm2         ; Red accumulator
    xorpd xmm7, xmm7         ; Weight sum
    
    ; Inicjalizacja outer loop (i = -radius)
    mov r12d, [rbp-36]       ; Za�adowanie promienia
    neg r12d                 ; i = -radius
    
outer_loop:
    ; Inicjalizacja inner loop (j = -radius)
    mov r13d, [rbp-36]       ; Za�adowanie promienia
    neg r13d                 ; j = -radius
    
inner_loop:
    mov eax, [rbp-40]        ; Pobranie x
    add eax, r13d            ; x + j
    mov ebx, [rbp-44]        ; Pobranie y
    add ebx, r12d            ; y + i
    
    ; Czy piksel jest w granicach obrazu?
    test eax, eax            ; Czy x >= 0
    js pixel_bounds_check_failed
    cmp eax, [rbp-48]        ; Sprawdzenie czy x < szeroko��
    jge pixel_bounds_check_failed
    test ebx, ebx            ; Sprawdzenie czy y >= 0
    js pixel_bounds_check_failed
    mov ecx, [rbp-28]        ; Pobranie stride
    shr ecx, 2               ; Konwersja stride na piksele (dzielenie przez 4)
    cmp ebx, ecx             ; Sprawdzenie czy y < wysoko��
    jge pixel_bounds_check_failed
    
    ; Liczenie offsetu piksela
    mov ecx, ebx
    imul ecx, [rbp-28]        ; y * szeroko�� linii
    lea ecx, [ecx + eax*4]    ; x * 4
    
    ; Pobranie warto�ci RGB
    mov rsi, [rbp-16]        ; Pobranie wska�nika do orygina�u
    movzx r14d, byte ptr [rsi + rcx]      ; Blue
    movzx r15d, byte ptr [rsi + rcx + 1]  ; Green
    movzx ebx, byte ptr [rsi + rcx + 2]   ; Red
    
    ; Calculate kernel index
    mov eax, r12d
    add eax, [rbp-36]        ; Dodanie promienia
    imul eax, [rbp-32]       ; Mno�enie przez rozmiar maski
    add eax, r13d
    add eax, [rbp-36]        ; Dodanie promienia
    
    ; Pobranie wagi z maski Gaussa
    mov rsi, [rbp-8]         ; Pobranie wska�nika do maski
    movsd xmm4, qword ptr [rsi + rax*8]
    
    ; Akumulacja wa�onych warto�ci kolor�w
    cvtsi2sd xmm5, r14d
    mulsd xmm5, xmm4
    addsd xmm0, xmm5         ; Blue
    
    cvtsi2sd xmm5, r15d
    mulsd xmm5, xmm4
    addsd xmm1, xmm5         ; Green
    
    cvtsi2sd xmm5, ebx
    mulsd xmm5, xmm4
    addsd xmm2, xmm5         ; Red
    
    addsd xmm7, xmm4         ; Dodanie wagi do sumy wag 
    
pixel_bounds_check_failed:      ;Dla pikseli poza granicami
    ; dla inner loop
    inc r13d                    ;Inkrementacja j
    mov eax, [rbp-36]           ; Pobranie promienia
    cmp r13d, eax               ;Por�wnanie j z promieniem
    jle inner_loop              ;Je�li j <= promie�, kontynuuj p�tl� wewn�trzn�
    
    ; dla outer loop
    inc r12d                    ;Inkrementacja i
    mov eax, [rbp-36]           ;Pobranie promienia
    cmp r12d, eax               ;Por�wnanie i z promieniem
    jle outer_loop              ;Je�li i <= promie�, kontynuuj p�tl� zewn�trzn�
    
    ; Sprawdzenie, czy mamy jakie� wa�ne pr�bki
    xorpd xmm6, xmm6
    ucomisd xmm7, xmm6      ; Czy suma wag == 0?
    je no_valid_samples     ; Je�li tak, u�ywamy oryginalnego piksela
    
    ; Normalizacja warto�ci pikseli
    movsd xmm5, xmm7        ; kopia sumy wag
    divsd xmm0, xmm5        ; Blue
    divsd xmm1, xmm5        ; Green
    divsd xmm2, xmm5        ; Red
    
    mulss xmm0, xmm5
    mulss xmm1, xmm5
    mulss xmm2, xmm5
    
    ; Konwersja na int
    cvttsd2si eax, xmm0      
    mov r12b, al
    cvttsd2si eax, xmm1
    mov r13b, al
    cvttsd2si eax, xmm2
    mov r14b, al
    jmp write_final_pixel

no_valid_samples:       ;Obs�uga b��du, gdy nie ma pikseli
    mov eax, [rbp-44]        ; Pobranie y
    imul eax, [rbp-28]       ; Mno�enie przez stride
    mov ecx, [rbp-40]        ; Pobranie x
    lea ecx, [ecx*4]         ; Obliczenie offsetu (x*4)
    add eax, ecx
    mov rsi, [rbp-16]        ; Pobranie wska�nika do orygina�u
    movzx r12d, byte ptr [rsi + rax]      ; Blue
    movzx r13d, byte ptr [rsi + rax + 1]  ; Green
    movzx r14d, byte ptr [rsi + rax + 2]  ; Red

    ; Zapis ko�cowego piksela
write_final_pixel: 
    ;Obliczenie pozycji docelowej piksela
    mov eax, [rbp-44]        ; Pobranie y
    imul eax, [rbp-28]       ; Mno�enie przez stride
    mov ecx, [rbp-40]        ; Pobranie x
    lea ecx, [ecx*4]         ; Obliczenie offsetu (x*4)
    add eax, ecx             ; Dodanie offsetu x
    
    ; Zapisz warto�ci do bufora wynikowego
    mov rdi, [rbp-24]        
    mov [rdi + rax], r12b      ; Blue
    mov [rdi + rax + 1], r13b  ; Green
    mov [rdi + rax + 2], r14b  ; Red
    mov byte ptr [rdi + rax + 3], 255  ; Alpha
    
    ; Przywr�cenie warto�ci rejestr�w i zwr�cenie wyniku
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    mov rsp, rbp
    pop rbp
    ret
ApplyGaussianBlurAsm ENDP
END