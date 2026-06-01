window.cameraInterop = {
    startCamera: async function (videoElementId, dotnetHelper) {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
            const video = document.getElementById(videoElementId);
            video.srcObject = stream;
            video.play();

            // THE LIVE LOOP: Runs every 1000 milliseconds (1 second)
            setInterval(() => {
                if (video.readyState === video.HAVE_ENOUGH_DATA) {
                    const canvas = document.createElement('canvas');
                    // Shrinking the image slightly so it sends over the network extremely fast
                    canvas.width = 480; 
                    canvas.height = 640; 
                    
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                    
                    // Getting the image and send it directly back to the C# code
                    const base64 = canvas.toDataURL('image/jpeg', 0.5).split(',')[1];
                    dotnetHelper.invokeMethodAsync('ProcessLiveFrame', base64);
                }
            }, 1000); 

        } catch (err) {
            console.error("Camera error: ", err);
            alert("Could not access the camera. Please allow camera permissions.");
        }
    }
};