import { Pressable, StyleSheet, Text, View } from 'react-native';
import { LinkedDevice } from '../api';

type Props = {
  device: LinkedDevice;
  onBack: () => void;
};

// Web (and any build without the native BLE module) cannot do Bluetooth
// provisioning. Metro picks this file on web so the native ESP module is never
// bundled there. Shows how to set up Wi-Fi for now.
export default function ProvisioningScreen({ device, onBack }: Props) {
  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>‹ Settings</Text>
      </Pressable>
      <Text style={styles.title}>Connect to Wi-Fi</Text>
      <View style={styles.card}>
        <Text style={styles.body}>
          Bluetooth setup runs on the phone app (the Areg dev/store build), not in
          this web preview.
        </Text>
        <Text style={styles.body}>For now you can set up the toy&apos;s Wi-Fi with the free</Text>
        <Text style={styles.strong}>ESP BLE Provisioning</Text>
        <Text style={styles.body}>app — pairing code (PoP):</Text>
        <Text style={styles.code}>areg-pair</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a', marginBottom: 16 },
  card: { borderWidth: 1, borderColor: '#e2e2e2', borderRadius: 10, padding: 16, backgroundColor: '#fafafa' },
  body: { color: '#444', fontSize: 15, marginBottom: 6 },
  strong: { color: '#2c4a7a', fontSize: 16, fontWeight: '700', marginVertical: 2 },
  code: { fontSize: 18, fontWeight: '700', color: '#a06a00', marginTop: 4 },
});
